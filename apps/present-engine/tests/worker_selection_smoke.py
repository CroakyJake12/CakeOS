#!/usr/bin/env python3
"""Prove snapshot-ref selection maps to stable live HUI geometry events."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

from worker_protocol_smoke import WorkerClient, require_semantic_mutation_events


def find_ref(response: dict[str, Any], expected_text: str) -> str:
    result = response.get("result", {})
    if result.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"element inventory overstates identity stability: {result}")
    for element in result.get("elements", []):
        if expected_text in [str(value) for value in element.get("text", [])]:
            ref = element.get("ref")
            if not isinstance(ref, str):
                raise RuntimeError(f"element ref is not a string: {element}")
            return ref
    raise RuntimeError(f"could not find element containing {expected_text!r}: {result}")


def selected_event(events: list[dict[str, Any]], ref: str) -> dict[str, Any]:
    matches = [
        event for event in events
        if event.get("event") == "elementSelectionChanged"
        and event.get("data", {}).get("selected") is True
        and event.get("data", {}).get("ref") == ref
    ]
    if not matches:
        raise RuntimeError(f"missing selected geometry event for {ref}: {events}")
    event = matches[-1]
    data = event["data"]
    if data.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"selection event overstates reference stability: {event}")
    rect = data.get("rectTwips")
    if not isinstance(rect, dict):
        raise RuntimeError(f"selection event has no rectTwips: {event}")
    if int(rect.get("width", 0)) <= 0 or int(rect.get("height", 0)) <= 0:
        raise RuntimeError(f"selection event has non-positive bounds: {event}")
    if not isinstance(data.get("angleHundredthDegrees"), int):
        raise RuntimeError(f"selection event has no normalized angle: {event}")
    return event


def require_cleared(
    events: list[dict[str, Any]],
    ref: str,
    reason: str,
) -> None:
    matches = [
        event for event in events
        if event.get("event") == "elementSelectionChanged"
        and event.get("data", {}).get("selected") is False
        and event.get("data", {}).get("ref") == ref
        and event.get("data", {}).get("reason") == reason
    ]
    if not matches:
        raise RuntimeError(
            f"missing deselection event for {ref} with reason {reason!r}: {events}"
        )


def main() -> int:
    if len(sys.argv) != 4:
        print(
            "usage: worker_selection_smoke.py WORKER INPUT.odp OUTPUT.odp",
            file=sys.stderr,
        )
        return 2

    worker = Path(sys.argv[1]).resolve()
    source = Path(sys.argv[2]).resolve()
    output = Path(sys.argv[3]).resolve()
    output.unlink(missing_ok=True)

    client = WorkerClient(worker)
    try:
        hello, payload = client.require_ok("hello")
        if payload:
            raise RuntimeError("hello unexpectedly returned binary data")
        capabilities = set(hello["result"]["capabilities"])
        for capability in (
            "listElements",
            "selectElement",
            "clearElementSelection",
            "moveSlide",
            "saveAs",
        ):
            if capability not in capabilities:
                raise RuntimeError(f"worker is missing selection capability {capability}")
        if "elementSelectionChanged" not in hello["result"]["events"]:
            raise RuntimeError("worker does not advertise the stable selection event")

        opened, _ = client.require_ok("open", path=str(source))
        if [slide["name"] for slide in opened["result"]["slides"]] != ["Alpha", "Beta", "Gamma"]:
            raise RuntimeError("selection fixture did not open with the expected slide order")

        inventory, payload = client.require_ok("listElements", slideIndex=0)
        if payload:
            raise RuntimeError("listElements unexpectedly returned binary data")
        alpha_ref = find_ref(inventory, "Alpha")

        # Clear any open/render events before testing selection correlation.
        client.require_ok("listSlides")
        client.take_events()

        selected, payload = client.require_ok("selectElement", ref=alpha_ref)
        if payload:
            raise RuntimeError("selectElement unexpectedly returned binary data")
        if selected["result"].get("ref") != alpha_ref or selected["result"].get("selected") is not True:
            raise RuntimeError(f"selectElement returned invalid metadata: {selected}")

        client.require_ok("listSlides")
        selection_events = client.take_events()
        first_geometry = selected_event(selection_events, alpha_ref)

        cleared, payload = client.require_ok("clearElementSelection")
        if payload or cleared["result"].get("selected") is not False:
            raise RuntimeError(f"clearElementSelection returned invalid metadata: {cleared}")
        client.require_ok("listSlides")
        clear_events = client.take_events()
        require_cleared(clear_events, alpha_ref, "explicitClear")

        # Re-select, then mutate the document. The worker must invalidate both
        # the saved snapshot and its snapshot-scoped live selection.
        client.require_ok("selectElement", ref=alpha_ref)
        client.require_ok("listSlides")
        selected_event(client.take_events(), alpha_ref)

        moved, _ = client.require_ok("moveSlide", fromIndex=0, toIndex=2)
        if [slide["name"] for slide in moved["result"]["slides"]] != ["Beta", "Gamma", "Alpha"]:
            raise RuntimeError("selection mutation did not reorder the fixture")

        stale, stale_payload = client.request("selectElement", ref=alpha_ref)
        if stale.get("ok") or stale_payload:
            raise RuntimeError("worker accepted a snapshot ref after document mutation")
        stale_message = str(stale.get("error", {}).get("message", ""))
        if "stale" not in stale_message or "save" not in stale_message:
            raise RuntimeError(f"worker did not explain stale selection ref: {stale}")
        mutation_events = client.take_events()
        require_cleared(mutation_events, alpha_ref, "documentMutation")
        require_semantic_mutation_events(mutation_events, "moveSlide")

        client.require_ok("saveAs", path=str(output), format="odp")
        if not output.exists() or output.stat().st_size == 0:
            raise RuntimeError("selection test did not persist reordered output")

        refreshed, _ = client.require_ok("listElements", slideIndex=0)
        beta_ref = find_ref(refreshed, "Beta")
        client.require_ok("selectElement", ref=beta_ref)
        client.require_ok("listSlides")
        refreshed_events = client.take_events()
        selected_event(refreshed_events, beta_ref)

        rect = first_geometry["data"]["rectTwips"]
        print("worker_element_selection=passed")
        print("worker_element_geometry=passed")
        print("worker_selection_staleness=passed")
        print(f"worker_selection_ref={alpha_ref}")
        print(
            "worker_selection_rect_twips="
            f"{rect['x']},{rect['y']},{rect['width']},{rect['height']}"
        )
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    raise SystemExit(main())

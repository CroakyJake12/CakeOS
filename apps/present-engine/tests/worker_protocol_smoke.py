#!/usr/bin/env python3
"""Exercise the Present worker's framed HUI boundary end to end."""

from __future__ import annotations

import json
import struct
import subprocess
import sys
from pathlib import Path
from typing import Any, BinaryIO

MAX_FRAME = 64 * 1024 * 1024


def read_exact(stream: BinaryIO, size: int) -> bytes:
    data = bytearray()
    while len(data) < size:
        chunk = stream.read(size - len(data))
        if not chunk:
            raise RuntimeError(f"worker stream ended after {len(data)} of {size} bytes")
        data.extend(chunk)
    return bytes(data)


def send_frame(stream: BinaryIO, metadata: dict[str, Any]) -> None:
    encoded = json.dumps(metadata, separators=(",", ":")).encode("utf-8")
    stream.write(struct.pack("<II", len(encoded), 0))
    stream.write(encoded)
    stream.flush()


def read_frame(stream: BinaryIO) -> tuple[dict[str, Any], bytes]:
    metadata_size, payload_size = struct.unpack("<II", read_exact(stream, 8))
    if metadata_size <= 0 or metadata_size > 1024 * 1024:
        raise RuntimeError(f"invalid worker metadata size: {metadata_size}")
    if payload_size > MAX_FRAME:
        raise RuntimeError(f"invalid worker payload size: {payload_size}")
    metadata = json.loads(read_exact(stream, metadata_size))
    payload = read_exact(stream, payload_size)
    return metadata, payload


class WorkerClient:
    def __init__(self, worker: Path) -> None:
        self.process = subprocess.Popen(
            [str(worker)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        if self.process.stdin is None or self.process.stdout is None:
            raise RuntimeError("failed to create Present worker pipes")
        self.stdin = self.process.stdin
        self.stdout = self.process.stdout
        self.next_id = 1

    def request(self, op: str, **fields: Any) -> tuple[dict[str, Any], bytes]:
        request_id = self.next_id
        self.next_id += 1
        send_frame(self.stdin, {"id": request_id, "op": op, **fields})
        response, payload = read_frame(self.stdout)
        if response.get("id") != request_id:
            raise RuntimeError(
                f"response id mismatch: expected {request_id}, got {response.get('id')}"
            )
        return response, payload

    def require_ok(self, op: str, **fields: Any) -> tuple[dict[str, Any], bytes]:
        response, payload = self.request(op, **fields)
        if not response.get("ok"):
            raise RuntimeError(f"{op} failed: {response.get('error')}")
        if response.get("protocolVersion") != 1:
            raise RuntimeError(f"unexpected protocol version: {response}")
        return response, payload

    def close(self) -> None:
        if self.process.poll() is None:
            try:
                self.require_ok("quit")
            finally:
                self.stdin.close()
        stderr = self.process.stderr.read().decode("utf-8", errors="replace") if self.process.stderr else ""
        code = self.process.wait(timeout=10)
        if code != 0:
            raise RuntimeError(f"worker exited with {code}: {stderr}")


def slide_count(response: dict[str, Any]) -> int:
    return len(response["result"]["slides"])


def main() -> int:
    if len(sys.argv) != 4:
        print(
            "usage: worker_protocol_smoke.py WORKER INPUT.odp OUTPUT.odp",
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
        result = hello["result"]
        required_capabilities = {
            "open",
            "listSlides",
            "slideExtent",
            "renderSlide",
            "addSlideAfter",
            "duplicateSlide",
            "deleteSlide",
            "undo",
            "redo",
            "saveAs",
        }
        missing = required_capabilities.difference(result["capabilities"])
        if missing:
            raise RuntimeError(f"worker is missing capabilities: {sorted(missing)}")
        if result["pixelTransport"] != "inline-binary-v1":
            raise RuntimeError(f"unexpected pixel transport: {result['pixelTransport']}")

        opened, _ = client.require_ok("open", path=str(source))
        if slide_count(opened) != 1:
            raise RuntimeError("worker did not open the one-slide fixture")

        extent, _ = client.require_ok("slideExtent", slideIndex=0)
        width = extent["result"]["widthTwips"]
        height = extent["result"]["heightTwips"]
        if width < 1000 or height < 1000:
            raise RuntimeError(f"worker returned unrealistic slide extent {width}x{height}")

        rendered, pixels = client.require_ok(
            "renderSlide", slideIndex=0, pixelWidth=320, pixelHeight=180
        )
        render_result = rendered["result"]
        if render_result["pixelWidth"] != 320 or render_result["pixelHeight"] != 180:
            raise RuntimeError(f"unexpected render dimensions: {render_result}")
        if render_result["pixelFormat"] not in {"RGBA", "BGRA"}:
            raise RuntimeError(f"unexpected pixel format: {render_result}")
        if len(pixels) != 320 * 180 * 4:
            raise RuntimeError(f"unexpected binary render size: {len(pixels)}")
        if len(set(pixels)) < 2:
            raise RuntimeError("worker render payload is uniform")

        rejected, rejected_payload = client.request(
            "renderSlide", slideIndex=0, pixelWidth=5000, pixelHeight=1
        )
        if rejected.get("ok") or rejected_payload:
            raise RuntimeError("worker did not reject an oversized render request")

        added, _ = client.require_ok("addSlideAfter", slideIndex=0)
        if slide_count(added) != 2:
            raise RuntimeError("worker addSlideAfter did not create a slide")

        undone, _ = client.require_ok("undo")
        if slide_count(undone) != 1:
            raise RuntimeError("worker undo did not restore one slide")

        redone, _ = client.require_ok("redo")
        if slide_count(redone) != 2:
            raise RuntimeError("worker redo did not restore the added slide")

        duplicated, _ = client.require_ok("duplicateSlide", slideIndex=0)
        if slide_count(duplicated) != 3:
            raise RuntimeError("worker duplicateSlide did not create a slide")

        deleted, _ = client.require_ok("deleteSlide", slideIndex=2)
        if slide_count(deleted) != 2:
            raise RuntimeError("worker deleteSlide did not remove a slide")

        client.require_ok("saveAs", path=str(output), format="odp")
        if not output.exists() or output.stat().st_size == 0:
            raise RuntimeError("worker saveAs did not create the output presentation")

        client.require_ok("close")
        reopened, _ = client.require_ok("open", path=str(output))
        if slide_count(reopened) != 2:
            raise RuntimeError("worker output did not persist two slides")

        print("worker_protocol=passed")
        print(f"worker_extent_twips={width}x{height}")
        print(f"worker_render_bytes={len(pixels)}")
        print("worker_saved_slides=2")
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    raise SystemExit(main())

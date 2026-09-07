from __future__ import annotations

import argparse
import json
from pathlib import Path

from .audit import audit_environment
from .broker import CompatibilityBroker, load_manifest


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="haven-compat")
    subparsers = parser.add_subparsers(dest="action", required=True)

    audit_parser = subparsers.add_parser("audit", help="read-only runtime prerequisite audit")
    audit_parser.add_argument("--runtime-root", type=Path)

    for action in ("plan", "launch", "reset"):
        action_parser = subparsers.add_parser(action)
        action_parser.add_argument("manifest", type=Path)

    args = parser.parse_args(argv)

    if args.action == "audit":
        print(json.dumps(audit_environment(args.runtime_root), indent=2))
        return 0

    broker = CompatibilityBroker()
    manifest = load_manifest(args.manifest)

    if args.action == "plan":
        plan = broker.plan(manifest)
        print(json.dumps({"backend": plan.backend, "argv": plan.argv, "env": plan.env, "prefix": plan.prefix_path}, indent=2))
        return 0
    if args.action == "launch":
        process = broker.launch(manifest)
        print(json.dumps({"pid": process.pid}))
        return 0

    broker.reset(manifest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

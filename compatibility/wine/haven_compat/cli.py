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

    subparsers.add_parser("capabilities", help="report the stable HUI-facing capability surface")

    health_parser = subparsers.add_parser("health", help="report backend and supervisor health")
    health_parser.add_argument("--runtime-root", type=Path)

    for action in ("plan", "launch", "status", "stop", "reset"):
        action_parser = subparsers.add_parser(action)
        action_parser.add_argument("manifest", type=Path)

    logs_parser = subparsers.add_parser("logs")
    logs_parser.add_argument("manifest", type=Path)
    logs_parser.add_argument("--lines", type=int, default=200)

    args = parser.parse_args(argv)

    if args.action == "audit":
        print(json.dumps(audit_environment(args.runtime_root), indent=2))
        return 0

    if args.action == "capabilities":
        print(json.dumps(CompatibilityBroker().capabilities(), indent=2))
        return 0

    if args.action == "health":
        print(json.dumps(CompatibilityBroker(runtime_root=args.runtime_root).health(), indent=2))
        return 0

    broker = CompatibilityBroker()
    manifest = load_manifest(args.manifest)

    if args.action == "plan":
        plan = broker.plan(manifest)
        print(json.dumps({
            "backend": plan.backend,
            "argv": plan.argv,
            "env": plan.env,
            "prefix": plan.prefix_path,
            "unit": broker.lifecycle_unit(manifest),
        }, indent=2))
        return 0

    if args.action == "launch":
        print(json.dumps(broker.launch(manifest).as_dict(), indent=2))
        return 0

    if args.action == "status":
        print(json.dumps(broker.status(manifest).as_dict(), indent=2))
        return 0

    if args.action == "stop":
        print(json.dumps(broker.stop(manifest).as_dict(), indent=2))
        return 0

    if args.action == "logs":
        print(json.dumps({
            "unit": broker.lifecycle_unit(manifest),
            "text": broker.logs(manifest, args.lines),
        }, indent=2))
        return 0

    broker.reset(manifest)
    print(json.dumps({"reset": True, "unit": broker.lifecycle_unit(manifest)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

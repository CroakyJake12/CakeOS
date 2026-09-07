from __future__ import annotations

import argparse
import json
from pathlib import Path

from .broker import CompatibilityBroker, load_manifest


def main() -> int:
    parser = argparse.ArgumentParser(prog="haven-compat")
    parser.add_argument("manifest", type=Path)
    parser.add_argument("action", choices=("plan", "launch", "reset"))
    args = parser.parse_args()

    broker = CompatibilityBroker()
    manifest = load_manifest(args.manifest)

    if args.action == "plan":
        plan = broker.plan(manifest)
        print(json.dumps({"backend": plan.backend, "argv": plan.argv, "env": plan.env, "prefix": plan.prefix_path}, indent=2))
        return 0
    if args.action == "launch":
        process = broker.launch(manifest)
        print(process.pid)
        return 0

    broker.reset(manifest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

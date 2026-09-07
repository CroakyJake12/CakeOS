from __future__ import annotations

import json
import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from .manifest import AppManifest


class CompatibilityError(RuntimeError):
    pass


@dataclass(frozen=True)
class LaunchPlan:
    backend: str
    argv: tuple[str, ...]
    env: dict[str, str]
    prefix_path: str


class CompatibilityBroker:
    """Policy-enforcing broker for optional Windows compatibility backends.

    Slice 1 implements Wine launch planning and execution. WinBoat remains an
    explicit unsupported provider rather than silently falling back to a less
    isolated path.
    """

    def __init__(self, state_root: Path | None = None, runtime_root: Path | None = None):
        data_home = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
        self.state_root = state_root or data_home / "haven" / "compat" / "wine" / "apps"
        self.runtime_root = runtime_root or data_home / "haven" / "compat" / "wine" / "runtimes"

    def plan(self, manifest: AppManifest) -> LaunchPlan:
        if manifest.backend == "winboat":
            raise CompatibilityError("WinBoat provider is not enabled in compatibility slice 1")
        if manifest.backend != "wine":
            raise CompatibilityError(f"unsupported backend: {manifest.backend}")

        bwrap = shutil.which("bwrap")
        if not bwrap:
            raise CompatibilityError("bubblewrap is required; refusing unsandboxed Wine execution")

        runtime_dir = (self.runtime_root / manifest.runtime).resolve()
        wine_bin = runtime_dir / "bin" / "wine"
        if not wine_bin.is_file():
            raise CompatibilityError(f"Wine runtime is unavailable: {wine_bin}")

        app_root = (self.state_root / manifest.app_id).resolve()
        prefix = app_root / "prefix"
        data = app_root / "data"
        prefix.mkdir(parents=True, exist_ok=True, mode=0o700)
        data.mkdir(parents=True, exist_ok=True, mode=0o700)

        argv: list[str] = [
            bwrap,
            "--die-with-parent",
            "--new-session",
            "--unshare-all",
            "--proc", "/proc",
            "--dev", "/dev",
            "--tmpfs", "/tmp",
            "--ro-bind", str(runtime_dir), "/opt/haven-wine",
            "--bind", str(prefix), "/var/lib/haven-wine/prefix",
            "--bind", str(data), "/var/lib/haven-wine/data",
        ]

        # Network is opt-in. bubblewrap's --unshare-all includes a private
        # network namespace; only an explicit grant re-shares host networking.
        if manifest.network != "none":
            argv.append("--share-net")

        for mount in manifest.mounts:
            flag = "--ro-bind" if mount.mode == "ro" else "--bind"
            argv.extend([flag, mount.source, mount.target])

        if manifest.gpu == "render":
            for render_node in _existing_render_nodes():
                argv.extend(["--dev-bind", render_node, render_node])

        # Audio output gets the PipeWire runtime directory only when granted.
        runtime_dir_env = os.environ.get("XDG_RUNTIME_DIR")
        if manifest.audio_output and runtime_dir_env:
            pipewire = Path(runtime_dir_env) / "pipewire-0"
            if pipewire.exists():
                argv.extend(["--ro-bind", str(pipewire), str(pipewire)])

        argv.extend([
            "--setenv", "WINEPREFIX", "/var/lib/haven-wine/prefix",
            "--setenv", "HOME", "/var/lib/haven-wine/data",
            "/opt/haven-wine/bin/wine",
            manifest.entrypoint,
        ])

        env = {
            "PATH": "/usr/bin:/bin",
            "LANG": os.environ.get("LANG", "C.UTF-8"),
        }
        for key in ("WAYLAND_DISPLAY", "DISPLAY", "XDG_RUNTIME_DIR"):
            if key in os.environ:
                env[key] = os.environ[key]

        return LaunchPlan(
            backend="wine",
            argv=tuple(argv),
            env=env,
            prefix_path=str(prefix),
        )

    def launch(self, manifest: AppManifest) -> subprocess.Popen[bytes]:
        plan = self.plan(manifest)
        return subprocess.Popen(plan.argv, env=plan.env, start_new_session=True)

    def reset(self, manifest: AppManifest) -> None:
        app_root = (self.state_root / manifest.app_id).resolve()
        if self.state_root.resolve() not in app_root.parents:
            raise CompatibilityError("refusing to reset outside compatibility state root")
        if app_root.exists():
            shutil.rmtree(app_root)


def load_manifest(path: Path) -> AppManifest:
    with path.open("r", encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise CompatibilityError("manifest root must be an object")
    return AppManifest.from_dict(value)


def _existing_render_nodes() -> Iterable[str]:
    dri = Path("/dev/dri")
    if not dri.is_dir():
        return ()
    return tuple(str(path) for path in sorted(dri.glob("renderD*")) if path.is_char_device())

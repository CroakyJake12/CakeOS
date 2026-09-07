from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import PurePosixPath
from typing import Any


class ManifestError(ValueError):
    pass


_ALLOWED_BACKENDS = {"wine", "winboat"}
_ALLOWED_NETWORK = {"none", "internet", "lan"}
_ALLOWED_GPU = {"none", "render"}
_ALLOWED_MOUNT_MODES = {"ro", "rw"}
_SHARE_ROOT = PurePosixPath("/mnt/haven-share")


def _is_safe_identifier(value: str) -> bool:
    return bool(value) and all(
        c in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-" for c in value
    ) and value not in {".", ".."}


@dataclass(frozen=True)
class MountGrant:
    source: str
    target: str
    mode: str = "ro"

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "MountGrant":
        if not isinstance(value, dict):
            raise ManifestError("each mount grant must be an object")
        source = str(value.get("source", "")).strip()
        target = str(value.get("target", "")).strip()
        mode = str(value.get("mode", "ro")).lower()
        if not source.startswith("/"):
            raise ManifestError("mount source must be an absolute host path")
        if not target.startswith("/"):
            raise ManifestError("mount target must be an absolute sandbox path")
        if mode not in _ALLOWED_MOUNT_MODES:
            raise ManifestError(f"unsupported mount mode: {mode}")
        if source in {"/", "/home", "/root", "/tmp"}:
            raise ManifestError("broad host filesystem mounts are forbidden")

        target_path = PurePosixPath(target)
        if ".." in target_path.parts:
            raise ManifestError("mount target traversal is forbidden")
        if target_path == _SHARE_ROOT or _SHARE_ROOT not in target_path.parents:
            raise ManifestError("mount targets must be beneath /mnt/haven-share/<name>")
        return cls(source=source, target=target, mode=mode)


@dataclass(frozen=True)
class AppManifest:
    app_id: str
    backend: str
    runtime: str
    entrypoint: str
    network: str = "none"
    clipboard: bool = False
    audio_output: bool = False
    microphone: bool = False
    gpu: str = "none"
    mounts: tuple[MountGrant, ...] = field(default_factory=tuple)

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "AppManifest":
        app_id = str(value.get("id", "")).strip()
        backend = str(value.get("backend", "wine")).lower()
        runtime = str(value.get("runtime", "")).strip()
        entrypoint = str(value.get("entrypoint", "")).strip()
        network = str(value.get("network", "none")).lower()
        gpu = str(value.get("gpu", "none")).lower()

        if not _is_safe_identifier(app_id):
            raise ManifestError("id must be a simple non-traversing identifier")
        if backend not in _ALLOWED_BACKENDS:
            raise ManifestError(f"unsupported backend: {backend}")
        if not _is_safe_identifier(runtime):
            raise ManifestError("runtime must be a simple non-traversing identifier")
        if not entrypoint:
            raise ManifestError("entrypoint is required")
        if network not in _ALLOWED_NETWORK:
            raise ManifestError(f"unsupported network policy: {network}")
        if gpu not in _ALLOWED_GPU:
            raise ManifestError(f"unsupported gpu policy: {gpu}")

        mounts_raw = value.get("mounts", [])
        if not isinstance(mounts_raw, list):
            raise ManifestError("mounts must be a list")
        mounts = tuple(MountGrant.from_dict(item) for item in mounts_raw)
        targets = [mount.target for mount in mounts]
        if len(targets) != len(set(targets)):
            raise ManifestError("mount targets must be unique")

        return cls(
            app_id=app_id,
            backend=backend,
            runtime=runtime,
            entrypoint=entrypoint,
            network=network,
            clipboard=bool(value.get("clipboard", False)),
            audio_output=bool(value.get("audioOutput", False)),
            microphone=bool(value.get("microphone", False)),
            gpu=gpu,
            mounts=mounts,
        )

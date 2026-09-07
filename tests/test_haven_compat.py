import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat.broker import CompatibilityBroker, CompatibilityError
from compatibility.wine.haven_compat.manifest import AppManifest, ManifestError


class ManifestTests(unittest.TestCase):
    def test_rejects_broad_host_mount(self):
        with self.assertRaises(ManifestError):
            AppManifest.from_dict({
                "id": "bad.app",
                "backend": "wine",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "mounts": [{"source": "/home", "target": "/share", "mode": "rw"}],
            })

    def test_defaults_to_no_network(self):
        manifest = AppManifest.from_dict({
            "id": "safe.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
        })
        self.assertEqual("none", manifest.network)
        self.assertFalse(manifest.clipboard)
        self.assertEqual("none", manifest.gpu)


class BrokerTests(unittest.TestCase):
    def _fixture(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        runtime_root = root / "runtimes"
        state_root = root / "apps"
        wine = runtime_root / "wine-11.0" / "bin" / "wine"
        wine.parent.mkdir(parents=True)
        wine.write_text("#!/bin/sh\n", encoding="utf-8")
        socket = root / "runtime" / "wayland-0"
        socket.parent.mkdir(parents=True)
        socket.touch()
        broker = CompatibilityBroker(state_root=state_root, runtime_root=runtime_root)
        manifest = AppManifest.from_dict({
            "id": "safe.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
        })
        return temp, root, broker, manifest

    def test_refuses_unsandboxed_execution(self):
        temp, root, broker, manifest = self._fixture()
        self.addCleanup(temp.cleanup)
        with patch("shutil.which", return_value=None):
            with self.assertRaisesRegex(CompatibilityError, "refusing unsandboxed"):
                broker.plan(manifest)

    def test_plan_keeps_network_private_by_default(self):
        temp, root, broker, manifest = self._fixture()
        self.addCleanup(temp.cleanup)
        env = {"XDG_RUNTIME_DIR": str(root / "runtime"), "WAYLAND_DISPLAY": "wayland-0"}
        with patch.dict(os.environ, env, clear=False), patch("shutil.which", return_value="/usr/bin/bwrap"):
            plan = broker.plan(manifest)
        self.assertNotIn("--share-net", plan.argv)
        self.assertIn("--unshare-all", plan.argv)
        self.assertIn(str(root / "runtime" / "wayland-0"), plan.argv)

    def test_network_requires_explicit_manifest_grant(self):
        temp, root, broker, _ = self._fixture()
        self.addCleanup(temp.cleanup)
        manifest = AppManifest.from_dict({
            "id": "online.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
            "network": "internet",
        })
        env = {"XDG_RUNTIME_DIR": str(root / "runtime"), "WAYLAND_DISPLAY": "wayland-0"}
        with patch.dict(os.environ, env, clear=False), patch("shutil.which", return_value="/usr/bin/bwrap"):
            plan = broker.plan(manifest)
        self.assertIn("--share-net", plan.argv)

    def test_winboat_is_explicitly_disabled_in_slice_one(self):
        temp, root, broker, _ = self._fixture()
        self.addCleanup(temp.cleanup)
        manifest = AppManifest.from_dict({
            "id": "vm.app",
            "backend": "winboat",
            "runtime": "winboat-0.9",
            "entrypoint": "app.exe",
        })
        with self.assertRaisesRegex(CompatibilityError, "not enabled"):
            broker.plan(manifest)

    def test_media_permissions_fail_closed(self):
        temp, root, broker, _ = self._fixture()
        self.addCleanup(temp.cleanup)
        manifest = AppManifest.from_dict({
            "id": "audio.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
            "audioOutput": True,
        })
        with self.assertRaisesRegex(CompatibilityError, "PipeWire media permissions"):
            broker.plan(manifest)


if __name__ == "__main__":
    unittest.main()

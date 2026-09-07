import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat.audit import evaluate_preflight
from compatibility.wine.haven_compat.broker import CompatibilityBroker, CompatibilityError
from compatibility.wine.haven_compat.lifecycle import UnitStatus, UserSystemdSupervisor, unit_name
from compatibility.wine.haven_compat.manifest import AppManifest, ManifestError


class FakeSupervisor:
    def __init__(self, running: bool = False):
        self.available = True
        self.running = running
        self.started = None

    def _status(self, app_id: str) -> UnitStatus:
        return UnitStatus(
            unit=unit_name(app_id),
            load_state="loaded" if self.running else "not-found",
            active_state="active" if self.running else "inactive",
            sub_state="running" if self.running else "dead",
            result=None,
            main_pid=1234 if self.running else None,
        )

    def start(self, app_id: str, launch_argv: tuple[str, ...], service_env: dict[str, str]) -> UnitStatus:
        self.started = (app_id, launch_argv, service_env)
        self.running = True
        return self._status(app_id)

    def status(self, app_id: str) -> UnitStatus:
        return self._status(app_id)

    def stop(self, app_id: str) -> UnitStatus:
        self.running = False
        return self._status(app_id)

    def logs(self, app_id: str, lines: int = 200) -> str:
        return f"{unit_name(app_id)}:{lines}"


class ManifestTests(unittest.TestCase):
    def test_rejects_broad_host_mount(self):
        with self.assertRaises(ManifestError):
            AppManifest.from_dict({
                "id": "bad.app",
                "backend": "wine",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "mounts": [{"source": "/home", "target": "/mnt/haven-share/home", "mode": "rw"}],
            })

    def test_rejects_mount_target_outside_share_root(self):
        with self.assertRaisesRegex(ManifestError, "haven-share"):
            AppManifest.from_dict({
                "id": "bad.target",
                "backend": "wine",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "mounts": [{"source": "/home/user/Documents", "target": "/var/lib/haven-wine/prefix", "mode": "rw"}],
            })

    def test_rejects_runtime_traversal(self):
        with self.assertRaisesRegex(ManifestError, "runtime"):
            AppManifest.from_dict({
                "id": "bad.runtime",
                "backend": "wine",
                "runtime": "../system",
                "entrypoint": "app.exe",
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
    def _fixture(self, supervisor=None):
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
        broker = CompatibilityBroker(state_root=state_root, runtime_root=runtime_root, supervisor=supervisor)
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

    def test_network_permissions_fail_closed(self):
        temp, root, broker, _ = self._fixture()
        self.addCleanup(temp.cleanup)
        manifest = AppManifest.from_dict({
            "id": "online.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
            "network": "internet",
        })
        with self.assertRaisesRegex(CompatibilityError, "network permissions"):
            broker.plan(manifest)

    def test_gpu_permission_requires_render_node(self):
        temp, root, broker, _ = self._fixture()
        self.addCleanup(temp.cleanup)
        manifest = AppManifest.from_dict({
            "id": "gpu.app",
            "backend": "wine",
            "runtime": "wine-11.0",
            "entrypoint": "app.exe",
            "gpu": "render",
        })
        env = {"XDG_RUNTIME_DIR": str(root / "runtime"), "WAYLAND_DISPLAY": "wayland-0"}
        with patch.dict(os.environ, env, clear=False), patch("shutil.which", return_value="/usr/bin/bwrap"), patch(
            "compatibility.wine.haven_compat.broker._existing_render_nodes", return_value=()
        ):
            with self.assertRaisesRegex(CompatibilityError, "no render node"):
                broker.plan(manifest)

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

    def test_launch_delegates_to_supervisor(self):
        supervisor = FakeSupervisor()
        temp, root, broker, manifest = self._fixture(supervisor=supervisor)
        self.addCleanup(temp.cleanup)
        env = {"XDG_RUNTIME_DIR": str(root / "runtime"), "WAYLAND_DISPLAY": "wayland-0"}
        with patch.dict(os.environ, env, clear=False), patch("shutil.which", return_value="/usr/bin/bwrap"):
            status = broker.launch(manifest)
        self.assertTrue(status.running)
        self.assertIsNotNone(supervisor.started)
        self.assertIn("--unshare-all", supervisor.started[1])

    def test_reset_refuses_running_application(self):
        supervisor = FakeSupervisor(running=True)
        temp, root, broker, manifest = self._fixture(supervisor=supervisor)
        self.addCleanup(temp.cleanup)
        with self.assertRaisesRegex(CompatibilityError, "stop it first"):
            broker.reset(manifest)

    def test_capabilities_advertise_only_enforced_network_policy(self):
        temp, root, broker, manifest = self._fixture(supervisor=FakeSupervisor())
        self.addCleanup(temp.cleanup)
        wine = broker.capabilities()["providers"]["wine"]
        self.assertEqual(["none"], wine["network"])
        self.assertFalse(wine["clipboard"])


class LifecycleTests(unittest.TestCase):
    def test_unit_name_is_stable_and_does_not_embed_app_id(self):
        first = unit_name("safe.app")
        self.assertEqual(first, unit_name("safe.app"))
        self.assertTrue(first.startswith("haven-compat-"))
        self.assertTrue(first.endswith(".service"))
        self.assertNotIn("safe.app", first)

    def test_start_command_clears_environment_and_owns_control_group(self):
        supervisor = UserSystemdSupervisor(
            systemd_run="/usr/bin/systemd-run",
            systemctl="/usr/bin/systemctl",
            env_bin="/usr/bin/env",
            journalctl="/usr/bin/journalctl",
        )
        command = supervisor.build_start_argv(
            "safe.app",
            ("/usr/bin/bwrap", "--unshare-all", "/opt/haven-wine/bin/wine", "app.exe"),
            {"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "SECRET": "must-not-leak"},
        )
        self.assertIn("--property=KillMode=control-group", command)
        self.assertIn("--collect", command)
        self.assertIn("/usr/bin/env", command)
        self.assertIn("-i", command)
        self.assertNotIn("SECRET=must-not-leak", command)
        self.assertIn("/usr/bin/bwrap", command)


class AuditTests(unittest.TestCase):
    def _wine_ready_facts(self):
        return {
            "platform": {"system": "Linux", "machine": "x86_64"},
            "commands": {
                "bwrap": "/usr/bin/bwrap",
                "systemd-run": "/usr/bin/systemd-run",
                "systemctl": "/usr/bin/systemctl",
                "journalctl": "/usr/bin/journalctl",
                "env": "/usr/bin/env",
                "podman": None,
                "docker": None,
                "freerdp": None,
            },
            "session": {"waylandSocketExists": True, "systemdUserReachable": True},
            "devices": {"kvmExists": False, "kvmReadable": False, "kvmWritable": False},
            "managedWineRuntimes": [{"id": "wine-11.0"}],
        }

    def test_wine_preflight_requires_managed_runtime(self):
        facts = self._wine_ready_facts()
        facts["managedWineRuntimes"] = []
        preflight = evaluate_preflight(facts)
        self.assertFalse(preflight["wineSlice1"]["prerequisitesPresent"])
        self.assertIn("managed-wine-runtime", preflight["wineSlice1"]["missing"])

    def test_wine_preflight_requires_user_systemd_manager(self):
        facts = self._wine_ready_facts()
        facts["session"]["systemdUserReachable"] = False
        preflight = evaluate_preflight(facts)
        self.assertFalse(preflight["wineSlice1"]["prerequisitesPresent"])
        self.assertIn("systemd-user-manager", preflight["wineSlice1"]["missing"])

    def test_wine_preflight_can_be_ready_without_winboat(self):
        preflight = evaluate_preflight(self._wine_ready_facts())
        self.assertTrue(preflight["wineSlice1"]["prerequisitesPresent"])
        self.assertFalse(preflight["winboatFuture"]["prerequisitesPresent"])


if __name__ == "__main__":
    unittest.main()

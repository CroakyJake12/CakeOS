import json
import os
import socket
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from compatibility.wine.haven_compat.daemon import (
    MAX_MESSAGE_BYTES,
    DaemonError,
    RequestError,
    _prepare_socket_parent,
    _remove_stale_socket,
    dispatch_request,
    serve_connection,
)
from compatibility.wine.haven_compat.lifecycle import UnitStatus, unit_name


_HEADER = struct.Struct("!I")
_LINUX_UID_SECURITY = hasattr(os, "geteuid")
_LINUX_PEERCRED = _LINUX_UID_SECURITY and hasattr(socket, "SO_PEERCRED") and hasattr(socket, "AF_UNIX")


class FakeBroker:
    def __init__(self):
        self.registered = None

    def capabilities(self):
        return {"providers": {"wine": {"enabled": True}}}

    def health(self):
        return {"ok": True}

    def list_apps(self):
        return []

    def register_app(self, manifest):
        self.registered = manifest
        return {"id": manifest.app_id}

    def unregister_app(self, app_id, delete_state=False):
        return {"id": app_id, "stateDeleted": delete_state}

    def launch_registered(self, app_id):
        return self._status(app_id, True)

    def status_registered(self, app_id):
        return self._status(app_id, False)

    def stop_registered(self, app_id):
        return self._status(app_id, False)

    def logs_registered(self, app_id, lines=200):
        return f"{app_id}:{lines}"

    def reset_registered(self, app_id):
        return None

    @staticmethod
    def _status(app_id, running):
        return UnitStatus(
            unit=unit_name(app_id),
            load_state="loaded" if running else "not-found",
            active_state="active" if running else "inactive",
            sub_state="running" if running else "dead",
            main_pid=123 if running else None,
        )


class DaemonDispatchTests(unittest.TestCase):
    def test_register_app_validates_manifest(self):
        broker = FakeBroker()
        result = dispatch_request(broker, {
            "method": "registerApp",
            "params": {
                "manifest": {
                    "id": "safe.app",
                    "backend": "wine",
                    "runtime": "wine-11.0",
                    "entrypoint": "app.exe",
                }
            },
        })
        self.assertEqual("safe.app", result["id"])
        self.assertEqual("safe.app", broker.registered.app_id)

    def test_method_rejects_unrelated_parameters(self):
        with self.assertRaisesRegex(RequestError, "unexpected parameters"):
            dispatch_request(FakeBroker(), {
                "method": "launch",
                "params": {"id": "safe.app", "lines": 10},
            })

    def test_unknown_method_fails_closed(self):
        with self.assertRaises(RequestError) as context:
            dispatch_request(FakeBroker(), {"method": "shell", "params": {}})
        self.assertEqual("method_not_found", context.exception.code)


@unittest.skipUnless(_LINUX_PEERCRED, "Linux SO_PEERCRED is required for daemon socket security tests")
class DaemonSocketTests(unittest.TestCase):
    def _round_trip(self, raw_payload: bytes, broker=None):
        client, server = socket.socketpair(socket.AF_UNIX, socket.SOCK_STREAM)
        self.addCleanup(client.close)
        self.addCleanup(server.close)
        client.sendall(_HEADER.pack(len(raw_payload)) + raw_payload)
        serve_connection(server, broker or FakeBroker())
        header = self._read_exact(client, _HEADER.size)
        (length,) = _HEADER.unpack(header)
        payload = self._read_exact(client, length)
        return json.loads(payload.decode("utf-8"))

    def test_same_user_socket_round_trip(self):
        response = self._round_trip(json.dumps({"method": "capabilities", "params": {}}).encode("utf-8"))
        self.assertTrue(response["ok"])
        self.assertTrue(response["result"]["providers"]["wine"]["enabled"])

    def test_malformed_json_gets_structured_error(self):
        response = self._round_trip(b"{not-json")
        self.assertFalse(response["ok"])
        self.assertEqual("invalid_request", response["error"]["code"])

    def test_oversized_response_is_replaced_with_protocol_error(self):
        class LargeBroker(FakeBroker):
            def capabilities(self):
                return {"data": "x" * (MAX_MESSAGE_BYTES + 100)}

        response = self._round_trip(json.dumps({"method": "capabilities"}).encode("utf-8"), LargeBroker())
        self.assertFalse(response["ok"])
        self.assertEqual("response_too_large", response["error"]["code"])

    @staticmethod
    def _read_exact(connection, size):
        output = b""
        while len(output) < size:
            output += connection.recv(size - len(output))
        return output


@unittest.skipUnless(_LINUX_UID_SECURITY, "POSIX effective-UID semantics are required for daemon path security tests")
class DaemonPathTests(unittest.TestCase):
    def test_socket_parent_must_stay_under_xdg_runtime_dir(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            runtime = root / "runtime"
            runtime.mkdir()
            outside = root / "outside"
            with patch.dict(os.environ, {"XDG_RUNTIME_DIR": str(runtime)}, clear=False):
                with self.assertRaisesRegex(DaemonError, "beneath XDG_RUNTIME_DIR"):
                    _prepare_socket_parent(outside)

    def test_regular_file_is_never_replaced_as_stale_socket(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "compat.sock"
            path.write_text("do not delete", encoding="utf-8")
            with self.assertRaisesRegex(DaemonError, "non-owned or non-socket"):
                _remove_stale_socket(path)
            self.assertTrue(path.is_file())


if __name__ == "__main__":
    unittest.main()

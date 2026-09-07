#!/usr/bin/env python3
"""Haven Slice 0 inference broker for a single local llama.cpp worker.

The broker deliberately owns the HUI-facing API. llama-server is an implementation
 detail reached only over a private Unix-domain socket. No model downloader is
included in Slice 0.
"""
from __future__ import annotations

import hashlib
import http.client
import http.server
import json
import os
import pathlib
import signal
import socket
import socketserver
import subprocess
import threading
import time
import urllib.parse
from dataclasses import dataclass
from typing import Any

from gguf import GgufValidationError, read_gguf_version

RUNTIME_DIR = pathlib.Path(os.environ.get("XDG_RUNTIME_DIR", "/tmp")) / "haven"
DATA_HOME = pathlib.Path(os.environ.get("XDG_DATA_HOME", pathlib.Path.home() / ".local/share")) / "haven"
MODEL_ROOT = DATA_HOME / "models"
MANIFEST_ROOT = MODEL_ROOT / "manifests"
BLOB_ROOT = MODEL_ROOT / "blobs" / "sha256"
BROKER_SOCKET = pathlib.Path(os.environ.get("HAVEN_INFERENCE_SOCKET", RUNTIME_DIR / "inference.sock"))
WORKER_SOCKET = pathlib.Path(os.environ.get("HAVEN_LLAMA_WORKER_SOCKET", RUNTIME_DIR / "llamacpp-worker.sock"))
WORKER_HOME = RUNTIME_DIR / "worker-home"
LLAMA_SERVER = pathlib.Path(os.environ.get("HAVEN_LLAMA_SERVER", "/usr/lib/haven/llama.cpp/llama-server"))
START_TIMEOUT_SECONDS = float(os.environ.get("HAVEN_LLAMA_START_TIMEOUT", "30"))


class BrokerError(RuntimeError):
    pass


@dataclass(frozen=True)
class ModelManifest:
    model_id: str
    display_name: str
    sha256: str
    size: int
    relative_blob: str
    license_id: str
    source: str
    gguf_versions: tuple[int, ...]

    @property
    def blob_path(self) -> pathlib.Path:
        candidate = (BLOB_ROOT / self.relative_blob).resolve()
        root = BLOB_ROOT.resolve()
        if candidate != root and root not in candidate.parents:
            raise BrokerError("manifest blob path escapes the content-addressed store")
        return candidate


def _json(path: pathlib.Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise BrokerError(f"{path.name}: expected a JSON object")
    return value


def load_manifests() -> dict[str, ModelManifest]:
    result: dict[str, ModelManifest] = {}
    if not MANIFEST_ROOT.exists():
        return result
    for path in sorted(MANIFEST_ROOT.glob("*.json")):
        raw = _json(path)
        required = ("id", "displayName", "sha256", "size", "blob", "license", "source", "ggufVersions")
        missing = [key for key in required if key not in raw]
        if missing:
            raise BrokerError(f"{path.name}: missing {', '.join(missing)}")
        digest = str(raw["sha256"]).lower()
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise BrokerError(f"{path.name}: invalid sha256")
        model_id = str(raw["id"])
        if not model_id or model_id in result:
            raise BrokerError(f"{path.name}: invalid or duplicate model id")
        gguf_versions = tuple(int(v) for v in raw["ggufVersions"])
        if not gguf_versions or any(v not in (2, 3) for v in gguf_versions):
            raise BrokerError(f"{path.name}: Slice 0 manifests may declare only GGUF versions 2 and 3")
        result[model_id] = ModelManifest(
            model_id=model_id,
            display_name=str(raw["displayName"]),
            sha256=digest,
            size=int(raw["size"]),
            relative_blob=str(raw["blob"]),
            license_id=str(raw["license"]),
            source=str(raw["source"]),
            gguf_versions=gguf_versions,
        )
    return result


def verify_blob(manifest: ModelManifest) -> pathlib.Path:
    path = manifest.blob_path
    if not path.is_file():
        raise BrokerError("model blob is not installed")
    stat = path.stat()
    if stat.st_size != manifest.size:
        raise BrokerError("model size does not match its manifest")
    try:
        version = read_gguf_version(path)
    except GgufValidationError as exc:
        raise BrokerError(str(exc)) from exc
    if version not in manifest.gguf_versions:
        raise BrokerError(f"GGUF version {version} is not allowed by this model manifest")
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != manifest.sha256:
        raise BrokerError("model sha256 does not match its manifest")
    return path


class UnixHTTPConnection(http.client.HTTPConnection):
    def __init__(self, unix_path: pathlib.Path, timeout: float = 3600):
        super().__init__("localhost", timeout=timeout)
        self.unix_path = str(unix_path)

    def connect(self) -> None:
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(self.unix_path)
        self.sock = sock


class Worker:
    def __init__(self) -> None:
        self._process: subprocess.Popen[bytes] | None = None
        self._model: ModelManifest | None = None
        self._lock = threading.RLock()

    @property
    def model(self) -> ModelManifest | None:
        with self._lock:
            return self._model

    @property
    def ready(self) -> bool:
        with self._lock:
            return self._process is not None and self._process.poll() is None and WORKER_SOCKET.exists()

    def load(self, manifest: ModelManifest) -> None:
        model_path = verify_blob(manifest)
        with self._lock:
            if self.ready and self._model and self._model.model_id == manifest.model_id:
                return
            self._stop_locked()
            if not LLAMA_SERVER.is_file():
                raise BrokerError(f"llama-server is unavailable at {LLAMA_SERVER}")
            WORKER_SOCKET.unlink(missing_ok=True)
            WORKER_HOME.mkdir(mode=0o700, parents=True, exist_ok=True)
            args = [
                str(LLAMA_SERVER),
                "--model", str(model_path),
                "--host", str(WORKER_SOCKET),
                "--no-ui",
                "--parallel", "1",
            ]
            env = os.environ.copy()
            env["HOME"] = str(WORKER_HOME)
            env.pop("HTTP_PROXY", None)
            env.pop("HTTPS_PROXY", None)
            env.pop("ALL_PROXY", None)
            self._process = subprocess.Popen(
                args,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                env=env,
                close_fds=True,
                start_new_session=True,
            )
            deadline = time.monotonic() + START_TIMEOUT_SECONDS
            while time.monotonic() < deadline:
                if self._process.poll() is not None:
                    stderr = (self._process.stderr.read(8192) if self._process.stderr else b"").decode("utf-8", "replace")
                    self._process = None
                    raise BrokerError(f"llama-server exited during startup: {stderr.strip()}")
                if WORKER_SOCKET.exists():
                    try:
                        conn = UnixHTTPConnection(WORKER_SOCKET, timeout=1)
                        conn.request("GET", "/health")
                        response = conn.getresponse()
                        response.read()
                        conn.close()
                        if 200 <= response.status < 300:
                            self._model = manifest
                            return
                    except OSError:
                        pass
                time.sleep(0.1)
            self._stop_locked()
            raise BrokerError("llama-server did not become healthy before the startup deadline")

    def unload(self) -> None:
        with self._lock:
            self._stop_locked()

    def _stop_locked(self) -> None:
        process = self._process
        self._process = None
        self._model = None
        if process is not None and process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGTERM)
                process.wait(timeout=5)
            except (ProcessLookupError, subprocess.TimeoutExpired):
                if process.poll() is None:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    process.wait(timeout=2)
        WORKER_SOCKET.unlink(missing_ok=True)


WORKER = Worker()
ACTIVE_REQUESTS: dict[str, threading.Event] = {}
ACTIVE_LOCK = threading.Lock()


class ThreadingUnixServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    address_family = socket.AF_UNIX
    daemon_threads = True

    def server_bind(self) -> None:
        pathlib.Path(self.server_address).unlink(missing_ok=True)
        super().server_bind()
        os.chmod(self.server_address, 0o600)


class Handler(http.server.BaseHTTPRequestHandler):
    server_version = "HavenInference/0"

    def log_message(self, fmt: str, *args: Any) -> None:
        # Deliberately never log request bodies/prompts.
        print(f"haven-inference: {self.address_string()} {fmt % args}")

    def _send_json(self, status: int, value: Any) -> None:
        body = json.dumps(value, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self) -> dict[str, Any]:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError as exc:
            raise BrokerError("invalid Content-Length") from exc
        if length < 0 or length > 16 * 1024 * 1024:
            raise BrokerError("request body is too large")
        raw = self.rfile.read(length)
        value = json.loads(raw or b"{}")
        if not isinstance(value, dict):
            raise BrokerError("expected a JSON object")
        return value

    def do_GET(self) -> None:
        try:
            if self.path == "/health":
                self._send_json(200, {"status": "ok", "provider": "llamacpp", "workerReady": WORKER.ready})
                return
            if self.path == "/v1/models":
                manifests = load_manifests()
                current = WORKER.model.model_id if WORKER.model else None
                self._send_json(200, {"models": [
                    {
                        "id": item.model_id,
                        "displayName": item.display_name,
                        "provider": "llamacpp",
                        "installed": item.blob_path.is_file(),
                        "loaded": item.model_id == current,
                        "license": item.license_id,
                        "source": item.source,
                        "ggufVersions": item.gguf_versions,
                    }
                    for item in manifests.values()
                ]})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def do_POST(self) -> None:
        try:
            parsed = urllib.parse.urlparse(self.path)
            segments = [segment for segment in parsed.path.split("/") if segment]
            if len(segments) == 4 and segments[:2] == ["v1", "models"] and segments[3] in {"load", "unload"}:
                model_id = urllib.parse.unquote(segments[2])
                manifests = load_manifests()
                if model_id not in manifests:
                    self._send_json(404, {"error": "model_not_found"})
                    return
                if segments[3] == "load":
                    WORKER.load(manifests[model_id])
                    self._send_json(200, {"status": "ready", "model": model_id})
                else:
                    if WORKER.model and WORKER.model.model_id == model_id:
                        WORKER.unload()
                    self._send_json(200, {"status": "unloaded", "model": model_id})
                return
            if parsed.path == "/v1/chat/completions":
                self._proxy_chat()
                return
            if len(segments) == 4 and segments[:2] == ["v1", "requests"] and segments[3] == "cancel":
                request_id = urllib.parse.unquote(segments[2])
                with ACTIVE_LOCK:
                    event = ACTIVE_REQUESTS.get(request_id)
                if event is None:
                    self._send_json(404, {"error": "request_not_found"})
                else:
                    event.set()
                    self._send_json(202, {"status": "cancelling", "requestId": request_id})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def _proxy_chat(self) -> None:
        if not WORKER.ready or WORKER.model is None:
            raise BrokerError("no llama.cpp model is loaded")
        payload = self._read_json()
        request_id = str(payload.pop("request_id", "")).strip()
        if not request_id or len(request_id) > 128:
            raise BrokerError("request_id is required and must be at most 128 characters")
        payload["model"] = WORKER.model.model_id
        payload["stream"] = True
        cancel = threading.Event()
        with ACTIVE_LOCK:
            if request_id in ACTIVE_REQUESTS:
                raise BrokerError("request_id is already active")
            ACTIVE_REQUESTS[request_id] = cancel
        conn: UnixHTTPConnection | None = None
        try:
            encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
            conn = UnixHTTPConnection(WORKER_SOCKET)
            conn.request("POST", "/v1/chat/completions", body=encoded, headers={"Content-Type": "application/json"})
            response = conn.getresponse()
            self.send_response(response.status)
            content_type = response.getheader("Content-Type", "text/event-stream")
            self.send_header("Content-Type", content_type)
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Haven-Request-Id", request_id)
            self.end_headers()
            while not cancel.is_set():
                chunk = response.read(4096)
                if not chunk:
                    break
                self.wfile.write(chunk)
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            cancel.set()
        finally:
            if conn is not None:
                conn.close()
            with ACTIVE_LOCK:
                ACTIVE_REQUESTS.pop(request_id, None)


def main() -> int:
    RUNTIME_DIR.mkdir(mode=0o700, parents=True, exist_ok=True)
    WORKER_HOME.mkdir(mode=0o700, parents=True, exist_ok=True)
    server = ThreadingUnixServer(str(BROKER_SOCKET), Handler)
    try:
        server.serve_forever(poll_interval=0.25)
    finally:
        WORKER.unload()
        server.server_close()
        BROKER_SOCKET.unlink(missing_ok=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

from __future__ import annotations

import hashlib
import json
import pathlib
import struct
import sys
import tempfile
import unittest
from unittest import mock

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import broker  # noqa: E402
import gguf  # noqa: E402


class GgufTests(unittest.TestCase):
    def _write(self, root: pathlib.Path, version: int, magic: bytes = b"GGUF") -> pathlib.Path:
        path = root / "model.gguf"
        path.write_bytes(magic + struct.pack("<I", version) + b"payload")
        return path

    def test_accepts_v2_and_v3(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self.assertEqual(2, gguf.read_gguf_version(self._write(root, 2)))
            self.assertEqual(3, gguf.read_gguf_version(self._write(root, 3)))

    def test_rejects_wrong_magic(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = self._write(pathlib.Path(tmp), 3, b"NOPE")
            with self.assertRaises(gguf.GgufValidationError):
                gguf.read_gguf_version(path)

    def test_rejects_future_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = self._write(pathlib.Path(tmp), 99)
            with self.assertRaises(gguf.GgufValidationError):
                gguf.read_gguf_version(path)


class ManifestTests(unittest.TestCase):
    def test_rejects_blob_escape(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "blobs"
            root.mkdir()
            manifest = broker.ModelManifest("id", "Model", "0" * 64, 8, "../escape.gguf", "MIT", "local", (3,))
            with mock.patch.object(broker, "BLOB_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    _ = manifest.blob_path

    def test_verify_blob_checks_gguf_size_and_hash(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "blobs"
            target = root / "aa" / "model.gguf"
            target.parent.mkdir(parents=True)
            payload = b"GGUF" + struct.pack("<I", 3) + b"payload"
            target.write_bytes(payload)
            digest = hashlib.sha256(payload).hexdigest()
            manifest = broker.ModelManifest("id", "Model", digest, len(payload), "aa/model.gguf", "MIT", "local", (3,))
            with mock.patch.object(broker, "BLOB_ROOT", root):
                self.assertEqual(target.resolve(), broker.verify_blob(manifest))

    def test_load_manifests_rejects_unapproved_gguf_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            manifest = {
                "id": "bad",
                "displayName": "Bad",
                "sha256": "0" * 64,
                "size": 1,
                "blob": "00/a.gguf",
                "license": "MIT",
                "source": "local",
                "ggufVersions": [4]
            }
            (root / "bad.json").write_text(json.dumps(manifest), encoding="utf-8")
            with mock.patch.object(broker, "MANIFEST_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    broker.load_manifests()


if __name__ == "__main__":
    unittest.main()

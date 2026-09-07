from __future__ import annotations

import copy
import json
import pathlib
import sys
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import backend_matrix  # noqa: E402


class BackendMatrixTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.path = RUNTIME / "backend-matrix.json"
        cls.matrix = json.loads(cls.path.read_text(encoding="utf-8"))

    def test_committed_matrix_is_valid_and_keeps_vm_and_gpu_claims_bounded(self) -> None:
        backend_matrix.validate_matrix(self.matrix)
        self.assertEqual("not-run", self.matrix["approvedVm"]["hardwareInspection"]["status"])
        by_id = {item["id"]: item for item in self.matrix["backends"]}
        cpu = by_id["cpu"]
        self.assertTrue(cpu["evidence"]["built"]["value"])
        self.assertTrue(cpu["evidence"]["tested"]["value"])
        self.assertTrue(cpu["evidence"]["runtimeProven"]["value"])
        self.assertFalse(cpu["evidence"]["benchmarked"]["value"])
        runtime = cpu["evidence"]["runtimeProven"]
        self.assertIn("GitHub-hosted Ubuntu x86_64 CI only", runtime["scope"])
        self.assertIn("Approved VM was not exercised", runtime["scope"])
        self.assertIn("34158175902", runtime["reference"])
        for backend_id in ("cuda", "hip", "vulkan", "sycl", "opencl", "openvino"):
            self.assertFalse(by_id[backend_id]["evidence"]["built"]["value"])
            self.assertFalse(by_id[backend_id]["evidence"]["runtimeProven"]["value"])
            self.assertFalse(by_id[backend_id]["evidence"]["benchmarked"]["value"])

    def test_runtime_proof_cannot_skip_tested(self) -> None:
        matrix = copy.deepcopy(self.matrix)
        cuda = next(item for item in matrix["backends"] if item["id"] == "cuda")
        cuda["evidence"]["runtimeProven"] = {"value": True, "scope": "bad", "reference": "test"}
        with self.assertRaises(backend_matrix.BackendMatrixError):
            backend_matrix.validate_matrix(matrix)

    def test_hardware_candidates_do_not_promote_runtime_evidence(self) -> None:
        probe = {"platform": {"system": "Linux"}, "gpus": [{"vendorName": "nvidia"}]}
        candidates = {item["id"]: item for item in backend_matrix.candidate_backends(probe, self.matrix)}
        self.assertTrue(candidates["cpu"]["hardwareCandidate"])
        self.assertTrue(candidates["cpu"]["runtimeProven"])
        self.assertTrue(candidates["cuda"]["hardwareCandidate"])
        self.assertFalse(candidates["cuda"]["runtimeProven"])


if __name__ == "__main__":
    unittest.main()

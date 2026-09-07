import unittest
from pathlib import Path


class PackagingContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).parents[1]
        cls.service = (root / "compatibility" / "wine" / "systemd" / "haven-compatd.service.in").read_text(encoding="utf-8")
        cls.packaging = (root / "compatibility" / "wine" / "PACKAGING.md").read_text(encoding="utf-8")

    def test_user_service_template_has_required_hardening(self):
        required = (
            "Type=simple",
            "ExecStart=@PYTHON@ -m compatibility.wine.haven_compat.cli daemon",
            "UMask=0077",
            "RuntimeDirectory=haven",
            "RuntimeDirectoryMode=0700",
            "NoNewPrivileges=yes",
            "PrivateTmp=yes",
            "ProtectSystem=strict",
            "ProtectHome=read-only",
            "ReadWritePaths=-%h/.local/share/haven/compat",
            "ProtectKernelTunables=yes",
            "ProtectKernelModules=yes",
            "ProtectControlGroups=yes",
            "LockPersonality=yes",
            "RestrictSUIDSGID=yes",
            "RestrictAddressFamilies=AF_UNIX",
        )
        for directive in required:
            with self.subTest(directive=directive):
                self.assertIn(directive, self.service)

    def test_template_does_not_request_root_or_shell_execution(self):
        forbidden = (
            "User=root",
            "Group=root",
            "AmbientCapabilities=",
            "CapabilityBoundingSet=CAP_",
            "ExecStart=/bin/sh",
            "ExecStart=/usr/bin/sh",
            "ExecStart=/bin/bash",
            "ExecStart=/usr/bin/bash",
        )
        for directive in forbidden:
            with self.subTest(directive=directive):
                self.assertNotIn(directive, self.service)

    def test_packaging_contract_keeps_vm_proof_and_bootstrap_explicit(self):
        required_text = (
            "$HOME/.local/share/haven/compat",
            "0700",
            "systemd-analyze --user verify",
            "another UID cannot successfully issue broker requests",
            "no Wine/WinBoat runtime claim",
        )
        for text in required_text:
            with self.subTest(text=text):
                self.assertIn(text, self.packaging)


if __name__ == "__main__":
    unittest.main()

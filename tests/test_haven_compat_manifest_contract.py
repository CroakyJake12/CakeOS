import json
import unittest
from pathlib import Path

from compatibility.wine.haven_compat.manifest import AppManifest, ManifestError


class ManifestContractTests(unittest.TestCase):
    def test_permission_strings_are_not_coerced_to_boolean(self):
        with self.assertRaisesRegex(ManifestError, "clipboard must be boolean"):
            AppManifest.from_dict({
                "id": "safe.app",
                "backend": "wine",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "clipboard": "false",
            })

    def test_unknown_manifest_fields_fail_closed(self):
        with self.assertRaisesRegex(ManifestError, "unexpected manifest fields"):
            AppManifest.from_dict({
                "id": "safe.app",
                "backend": "wine",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "hostAccess": true_value(),
            })

    def test_unknown_mount_fields_fail_closed(self):
        with self.assertRaisesRegex(ManifestError, "unexpected mount fields"):
            AppManifest.from_dict({
                "id": "safe.app",
                "runtime": "wine-11.0",
                "entrypoint": "app.exe",
                "mounts": [{
                    "source": "/home/user/Documents/App",
                    "target": "/mnt/haven-share/app",
                    "mode": "ro",
                    "passthrough": True,
                }],
            })

    def test_machine_readable_schema_matches_core_enums(self):
        schema_path = Path(__file__).parents[1] / "compatibility" / "wine" / "manifest.schema.json"
        schema = json.loads(schema_path.read_text(encoding="utf-8"))
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(["wine", "winboat"], schema["properties"]["backend"]["enum"])
        self.assertEqual(["none", "internet", "lan"], schema["properties"]["network"]["enum"])
        self.assertEqual(["none", "render"], schema["properties"]["gpu"]["enum"])
        self.assertFalse(schema["$defs"]["mountGrant"]["additionalProperties"])


def true_value():
    return True


if __name__ == "__main__":
    unittest.main()

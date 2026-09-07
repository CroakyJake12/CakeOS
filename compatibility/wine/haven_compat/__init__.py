"""HavenOS Windows compatibility broker.

The package is intentionally dependency-free. Runtime integrations such as Wine,
bubblewrap and systemd are discovered at launch time and are never installed by
this code.
"""

from .broker import CompatibilityBroker, CompatibilityError
from .manifest import AppManifest, ManifestError

__all__ = [
    "AppManifest",
    "CompatibilityBroker",
    "CompatibilityError",
    "ManifestError",
]

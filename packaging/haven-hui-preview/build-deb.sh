#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_HUI_PREVIEW_VERSION:-0.1.0}"
publish="$root/artifacts/hui-preview/publish"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+~.-][A-Za-z0-9.+~-]+)?$ ]] || { echo "Invalid Debian preview version: $version" >&2; exit 2; }
[[ -x "$publish/cakeos-hui-preview" ]] || { echo "Build HUI preview before packaging" >&2; exit 2; }

mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/hui-preview" "$stage/usr/bin" "$out"
cp -R "$publish/." "$stage/usr/lib/cakeos/hui-preview/"
cat > "$stage/usr/bin/cakeos-hui-preview" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/hui-preview/cakeos-hui-preview "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-hui-preview"

cat > "$stage/DEBIAN/control" <<EOF
Package: haven-hui-preview
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Depends: dotnet-runtime-10.0
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS HUI Linux preview smoke runtime
 Reproducible preview package for validating the pinned HUI core on Linux.
 This package is not a desktop session and does not replace GNOME Shell or Mutter.
EOF

package="$out/haven-hui-preview_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"
echo "Created $package"

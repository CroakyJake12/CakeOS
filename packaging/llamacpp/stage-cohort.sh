#!/usr/bin/env bash
set -euo pipefail

repo="CroakyJake12/CakeOS"
artifact_id="10031771129"
artifact_zip_sha256="33fe22cdf3b1ba0bc2b77fd9e28290189b93f72575c55f5e24ff898ddfdbb4f3"
deb_name="haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb"
deb_sha256="13ea16e4ffa92d1e4b2f31a1a32ba00dc8802ec9077733d7db96a68dad92142d"
stage_dir="platform/ubuntu/image/live-build/config/packages.chroot"

command -v gh >/dev/null 2>&1 || { echo "gh is required" >&2; exit 2; }
command -v sha256sum >/dev/null 2>&1 || { echo "sha256sum is required" >&2; exit 2; }
command -v unzip >/dev/null 2>&1 || { echo "unzip is required" >&2; exit 2; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

zip="$tmp/haven-llamacpp-runtime-amd64.zip"
extract="$tmp/extract"
mkdir -p "$extract"

echo "Fetching immutable GitHub Actions artifact ${artifact_id} from ${repo}..."
gh api \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  "repos/${repo}/actions/artifacts/${artifact_id}/zip" > "$zip"

echo "${artifact_zip_sha256}  ${zip}" | sha256sum -c -
unzip -q "$zip" -d "$extract"

deb="$extract/$deb_name"
[ -f "$deb" ] || { echo "expected package missing: $deb_name" >&2; exit 3; }
echo "${deb_sha256}  ${deb}" | sha256sum -c -

mkdir -p "$stage_dir"
cp -f "$deb" "$stage_dir/$deb_name"

echo "Staged verified model-free package:"
echo "  $stage_dir/$deb_name"
echo "  sha256=$deb_sha256"

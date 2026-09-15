#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bash "$root/HUI/stage-donor.sh"
python3 "$root/HUI/patches/apply-linux-migration-patches.py"

out="$root/artifacts/hui-linux-host/publish"
rm -rf "$out"
mkdir -p "$out"

dotnet publish "$root/HUI/LinuxHost/CakeOS.HuiLinuxHost.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$out" \
  -p:HavenUiProject="$root/HUI/vendor/Haven.UI/Haven.UI.csproj"

test -x "$out/cakeos-hui-linux-preview"
echo "Built graphical HUI Linux host at $out/cakeos-hui-linux-preview"
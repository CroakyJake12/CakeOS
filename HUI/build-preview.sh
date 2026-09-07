#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bash "$root/HUI/stage-donor.sh"

dotnet publish "$root/HUI/Preview/CakeOS.HuiPreview.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained false \
  -p:ContinuousIntegrationBuild=true \
  -o "$root/artifacts/hui-preview/publish"

"$root/artifacts/hui-preview/publish/cakeos-hui-preview" > "$root/artifacts/hui-preview/render-summary.json"

echo "Built HUI preview and wrote artifacts/hui-preview/render-summary.json"

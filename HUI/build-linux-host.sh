#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bash "$root/HUI/stage-donor.sh"
bash "$root/tests/verify-hui-preview.sh"

out="$root/artifacts/hui-linux-host/publish"
evidence="$root/artifacts/hui-linux-host/optional-runtime-components.txt"
rm -rf "$out"
mkdir -p "$out"

dotnet publish "$root/HUI/LinuxHost/CakeOS.HuiLinuxHost.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$out"

# The .NET self-contained payload includes an optional Linux LTTng tracepoint
# provider that is linked to the obsolete liblttng-ust.so.0 SONAME. Modern
# supported Ubuntu releases ship the incompatible liblttng-ust.so.1 ABI.
# CakeOS does not use LTTng tracing in this preview slice, so do not ship the
# optional provider rather than adding an unsafe compatibility symlink or
# accepting an unresolved ELF dependency.
trace_provider="$out/libcoreclrtraceptprovider.so"
if [[ -e "$trace_provider" ]]; then
  rm -f "$trace_provider"
  printf '%s\n' \
    'Pruned optional .NET LTTng diagnostics component: libcoreclrtraceptprovider.so' \
    'Reason: upstream payload requires obsolete liblttng-ust.so.0; CakeOS HUI preview does not enable LTTng tracing.' \
    > "$evidence"
else
  printf '%s\n' 'Optional .NET LTTng diagnostics component was not present in the publish payload.' > "$evidence"
fi

test ! -e "$trace_provider"
test -x "$out/cakeos-hui-linux-preview"
echo "Built graphical HUI Linux host at $out/cakeos-hui-linux-preview"

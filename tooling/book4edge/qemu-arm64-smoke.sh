#!/usr/bin/env bash
# Generic ARM64 QEMU boot smoke (handover section 29).
# Usage: qemu-arm64-smoke.sh <iso> <outdir> <wait-seconds>
# "virt" is NOT Book4 Edge hardware. Verdicts are GENERIC-ARM64-BOOT only.
set -u

ISO="${1:?ISO required}"
OUT="${2:?outdir required}"
WAIT="${3:-360}"
mkdir -p "$OUT"

SERIAL="$OUT/serial.log"
QLOG="$OUT/qemu.log"
SCREEN="$OUT/screen.ppm"
REPORT="$OUT/qemu-report.md"
MON="$OUT/mon.sock"

FW=""
for candidate in /usr/share/qemu-efi-aarch64/QEMU_EFI.fd /usr/share/AAVMF/AAVMF_CODE.fd; do
  if [ -f "$candidate" ]; then FW="$candidate"; break; fi
done

{
  echo "# Generic ARM64 QEMU smoke (NOT Book4 Edge hardware proof)"
  echo
  echo "- ISO: \`$ISO\`"
  echo "- Machine: qemu-system-aarch64, virt, EDK2, serial + VNC screendump"
  echo "- Date (UTC): $(date -u +%FT%TZ)"
  echo
} > "$REPORT"

if [ -z "$FW" ]; then
  echo "- VERDICT: QEMU-SMOKE-BLOCKED (no AAVMF/EDK2 firmware found)" >> "$REPORT"
  cat "$REPORT"
  exit 1
fi
echo "- Firmware: \`$FW\`" >> "$REPORT"

rm -f "$MON"
qemu-system-aarch64 \
  -M virt -cpu max -m 4096 \
  -bios "$FW" \
  -cdrom "$ISO" \
  -display none -vga none \
  -serial file:"$SERIAL" \
  -monitor unix:"$MON",server=nowait \
  -boot order=d \
  >"$QLOG" 2>&1 &
QEMU_PID=$!
echo "- QEMU PID: $QEMU_PID" >> "$REPORT"

sleep "$WAIT"

ALIVE="no"
if kill -0 "$QEMU_PID" 2>/dev/null; then ALIVE="yes"; fi
echo "- VM alive after ${WAIT}s: $ALIVE" >> "$REPORT"

# Framebuffer screendump via the monitor socket (best effort).
if [ -S "$MON" ]; then
  printf '%s\n' "screendump $SCREEN" "quit" | socat - "UNIX-CONNECT:$MON" >/dev/null 2>&1 || true
  sleep 2
fi
if [ -s "$SCREEN" ]; then
  echo "- Screendump: captured ($(stat -c%s "$SCREEN") bytes)" >> "$REPORT"
  if command -v pnmtopng >/dev/null 2>&1; then
    pnmtopng "$SCREEN" > "$OUT/screen.png" 2>/dev/null || true
  fi
else
  echo "- Screendump: NOT captured (no VGA/headless firmware output)" >> "$REPORT"
fi

kill "$QEMU_PID" 2>/dev/null || true
wait "$QEMU_PID" 2>/dev/null || true

echo >> "$REPORT"
echo "## Serial markers (generic boot-chain evidence)" >> "$REPORT"
for marker in "EDK II" "UEFI" "GRUB" "GNU GRUB" "Linux version" "casper" "Casper" "EFI stub"; do
  if [ -f "$SERIAL" ] && grep -qi "$marker" "$SERIAL"; then
    echo "- FOUND: $marker" >> "$REPORT"
  else
    echo "- absent: $marker" >> "$REPORT"
  fi
done

FOUND=0
if [ -f "$SERIAL" ]; then FOUND=$(grep -ci -e 'EDK II' -e UEFI -e GRUB -e 'Linux version' -e casper "$SERIAL" || true); fi
{
  echo
  if [ "$FOUND" -gt 0 ]; then
    echo "- VERDICT: GENERIC-ARM64-BOOT-PROGRESS (serial shows boot-chain activity; NOT Book4 Edge proof)"
  else
    echo "- VERDICT: QEMU-SMOKE-INCONCLUSIVE (no boot markers on serial; see screendump/log)"
  fi
  echo
  echo "Reminder: QEMU virt results must never be reported as Book4 Edge hardware support."
} >> "$REPORT"

cat "$REPORT"

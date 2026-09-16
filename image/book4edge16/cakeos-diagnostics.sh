#!/bin/sh
# CakeOS Book4 Edge 16" first-boot diagnostics bundle.
# Usage (morning validation, ONE command): sudo cakeos-diagnostics
# Output: timestamped tar.gz in /var/log/cakeos-diagnostics/
set -eu

outdir="/var/log/cakeos-diagnostics"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

collect() {
    # $1 = filename, $@ = command (run, capture stdout+stderr, never fail)
    name="$1"; shift
    "$@" >"$work/$name" 2>&1 || echo "EXIT=$?" >>"$work/$name"
}

collect uname.txt uname -a
collect dt-model.txt cat /proc/device-tree/model
collect dt-compatible.txt tr '\0' '\n' < /proc/device-tree/compatible
collect lsblk.txt lsblk -o NAME,SIZE,TYPE,MOUNTPOINT,MODEL
collect lspci.txt lspci -nn
collect lsusb.txt lsusb
collect ip-link.txt ip link
collect rfkill.txt rfkill list
collect input-devices.txt cat /proc/bus/input/devices
collect dmesg.txt dmesg
collect dmesg-qcom.txt sh -c 'dmesg | grep -Ei "qcom|qualcomm|x1e|adreno|ath12k|i2c-hid|hid-over|Goodix|ELAN|wcn|remoteproc|fastrpc|cdsp|adsp|venus|ipa|nvme|drm|panel|soundwire|pmic|spmi" || true'
collect libinput-list.txt libinput list-devices
collect dpkg-cakeos.txt sh -c 'dpkg -l | grep -i cakeos || true'
collect kernel-modules-qcom.txt sh -c 'lsmod | grep -Ei "qcom|ath12k|i2c_hid|hid|venus|remoteproc" || true'
collect firmware-qcom.txt sh -c 'ls -R /lib/firmware/qcom 2>/dev/null | head -100 || true'
collect dtb-loaded.txt sh -c 'ls -l /sys/firmware/devicetree/base/compatible /proc/device-tree 2>/dev/null; find /proc/device-tree -maxdepth 2 -name compatible | head -40 || true'
collect i2c-devices.txt sh -c 'ls -l /sys/bus/i2c/devices/ || true'
collect input-by-path.txt sh -c 'ls -l /dev/input/by-path/ 2>/dev/null || true'
collect remoteproc.txt sh -c 'for d in /sys/class/remoteproc/*; do echo "== $d"; cat "$d/name" "$d/state" "$d/firmware" 2>/dev/null; done; ls -l /dev/fastrpc* /dev/*cdsp* /dev/*adsp* 2>/dev/null || true'
collect npu-dmesg.txt sh -c 'dmesg | grep -Ei "remoteproc|fastrpc|cdsp|adsp|npu|dspqueue|aic100|qaic" || true'
collect wifi-fw.txt sh -c 'dmesg | grep -Ei "ath12k|wcn7850|board-2.bin|BDF|calibration|Direct firmware load.*failed" | head -30 || true'
collect usb-video.txt sh -c 'ls -l /dev/video* /dev/media* 2>/dev/null; v4l2-ctl --list-devices 2>/dev/null || true'
collect battery.txt sh -c 'for d in /sys/class/power_supply/*; do echo "== $d"; cat "$d/type" "$d/status" "$d/capacity" "$d/voltage_now" "$d/current_now" 2>/dev/null; done; upower -d 2>/dev/null | head -40 || true'
collect audio.txt sh -c 'aplay -l 2>/dev/null; arecord -l 2>/dev/null; wpctl status 2>/dev/null | head -30 || true'
collect asound.txt sh -c 'cat /proc/asound/cards 2>/dev/null; ls /dev/snd/ 2>/dev/null || true'
collect pipewire.txt sh -c 'systemctl --user status pipewire wireplumber 2>/dev/null | head -30; pactl info 2>/dev/null | head -15 || true'
collect bluetooth.txt sh -c 'bluetoothctl list 2>/dev/null; hciconfig -a 2>/dev/null; dmesg | grep -Ei "bluetooth|btusb|hci0|qca9377|wcn" | head -20 || true'
collect thermal.txt sh -c 'for z in /sys/class/thermal/thermal_zone*; do echo "== $z $(cat "$z/type" 2>/dev/null)"; cat "$z/temp" 2>/dev/null; done || true'
collect touch-evtest.txt sh -c 'evtest --list 2>/dev/null || libinput list-devices 2>/dev/null | grep -iE "touch|pen|finger" || true'

# Machine-readable presence summary. Statuses are DETECTED / NOT_DETECTED
# (detection only — never functional proof; no PASS without hardware test).
summary="$work/hwtest-summary.json"
present() { grep -Eq "$2" "$work/$1" 2>/dev/null; }
status_of() { if present "$@"; then printf 'DETECTED'; else printf 'NOT_DETECTED'; fi; }
{
    echo '{'
    echo '  "schema": 1,'
    echo "  \"stamp\": \"$stamp\","
    echo "  \"wifi_ath12k\": \"$(status_of dmesg-qcom.txt 'ath12k')\","
    echo "  \"bluetooth_hci\": \"$(status_of bluetooth.txt 'hci[0-9]|BD Address')\","
    echo "  \"audio_cards\": \"$(status_of asound.txt '[0-9] +\\[')\","
    echo "  \"touch_input\": \"$(status_of input-devices.txt 'Touchscreen|touchscreen|ELAN|Goodix|FTS')\","
    echo "  \"battery\": \"$(status_of battery.txt 'BAT|Battery')\","
    echo "  \"uvc_video\": \"$(status_of usb-video.txt '/dev/video')\","
    echo "  \"remoteproc\": \"$(status_of remoteproc.txt 'remoteproc[0-9]')\","
    echo "  \"fastrpc\": \"$(status_of remoteproc.txt 'fastrpc')\""
    echo '}'
} > "$summary"

if command -v cakeos-canvas-rnote >/dev/null 2>&1; then
    collect canvas-version.txt sh -c 'dpkg-query -W -f="${Version}\n" cakeos-canvas-rnote'
fi
if command -v cakeos-boards >/dev/null 2>&1; then
    collect boards-version.txt sh -c 'dpkg-query -W -f="${Version}\n" cakeos-boards'
fi

mkdir -p "$outdir"
tar -czf "$outdir/cakeos-diagnostics-$stamp.tar.gz" -C "$work" .
echo "Diagnostics bundle: $outdir/cakeos-diagnostics-$stamp.tar.gz"

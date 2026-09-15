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

if command -v cakeos-canvas-rnote >/dev/null 2>&1; then
    collect canvas-version.txt sh -c 'dpkg-query -W -f="${Version}\n" cakeos-canvas-rnote'
fi
if command -v cakeos-boards >/dev/null 2>&1; then
    collect boards-version.txt sh -c 'dpkg-query -W -f="${Version}\n" cakeos-boards'
fi

mkdir -p "$outdir"
tar -czf "$outdir/cakeos-diagnostics-$stamp.tar.gz" -C "$work" .
echo "Diagnostics bundle: $outdir/cakeos-diagnostics-$stamp.tar.gz"

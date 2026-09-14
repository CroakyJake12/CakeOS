#!/usr/bin/env bash
set -euo pipefail

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
OUT=${1:-"$ROOT/artifacts/cohort"}
VERSION=${VERSION:-0.1.0}
ARCH=${ARCH:-amd64}
mkdir -p "$OUT"
rm -rf "$OUT/work"
mkdir -p "$OUT/work"

require() { command -v "$1" >/dev/null 2>&1 || { echo "missing command: $1" >&2; exit 1; }; }
require dpkg-deb
require sha256sum

write_control() {
  local dir=$1 name=$2 depends=$3 description=$4
  mkdir -p "$dir/DEBIAN"
  cat > "$dir/DEBIAN/control" <<EOF
Package: $name
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: $depends
Maintainer: HavenOS
Description: $description
EOF
}

# Build Data app
build_data() {
  local dir="$OUT/work/havenos-data"
  mkdir -p "$dir/usr/lib/havenos/data" "$dir/usr/bin"
  cp -a "$ROOT/apps/Data/." "$dir/usr/lib/havenos/data/"
  cat > "$dir/usr/bin/haven-data-calc-worker" <<'EOF'
#!/bin/sh
set -eu
exec python3 /usr/lib/havenos/data/workers/calc_worker.py "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-data-calc-worker"
  cat > "$dir/usr/bin/haven-data-duckdb-worker" <<'EOF'
#!/bin/sh
set -eu
exec python3 /usr/lib/havenos/data/workers/duckdb_worker.py "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-data-duckdb-worker"
  write_control "$dir" havenos-data "python3, python3-duckdb, python3-uno, libreoffice-calc-nogui, libc6, libstdc++6, libgcc-s1" \
    "HavenOS Data Calc and DuckDB worker sources"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-data_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Present engine (native worker for Write/Present)
build_present_engine() {
  local dir="$OUT/work/havenos-present-engine"
  mkdir -p "$dir/usr/lib/havenos/present-engine" "$dir/usr/bin"
  test -n "${PRESENT_BUILD_DIR:-}" || { echo "PRESENT_BUILD_DIR is required" >&2; exit 1; }
  test -x "$PRESENT_BUILD_DIR/cakeos-present-worker" || { echo "Present worker binary is missing" >&2; exit 1; }
  cp "$PRESENT_BUILD_DIR/cakeos-present-worker" "$dir/usr/lib/havenos/present-engine/"
  cp -a "$ROOT/apps/present-engine/README.md" "$dir/usr/lib/havenos/present-engine/"
  cat > "$dir/usr/bin/cakeos-present-worker" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present-engine/cakeos-present-worker "$@"
EOF
  chmod 0755 "$dir/usr/bin/cakeos-present-worker"
  write_control "$dir" havenos-present-engine "libreoffice-impress-nogui, libstdc++6, libc6, libglib2.0-0" \
    "HavenOS Present Engine native worker (LibreOfficeKit)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-present-engine_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Write app
build_write() {
  local dir="$OUT/work/havenos-write"
  mkdir -p "$dir/usr/lib/havenos/write" "$dir/usr/bin"
  test -n "${WRITE_BUILD_DIR:-}" || { echo "WRITE_BUILD_DIR is required" >&2; exit 1; }
  test -x "$WRITE_BUILD_DIR/HavenOS.Write.App" || { echo "Write app binary is missing" >&2; exit 1; }
  cp -a "$WRITE_BUILD_DIR/." "$dir/usr/lib/havenos/write/"
  cat > "$dir/usr/bin/haven-write" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/write/HavenOS.Write.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-write"
  if [[ -f "$WRITE_BUILD_DIR/HavenOS.Write.Hui" ]]; then
    cat > "$dir/usr/bin/haven-write-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/write/HavenOS.Write.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-write-hui"
  fi
  write_control "$dir" havenos-write "havenos-present-engine, libreoffice-writer-nogui, libc6, libstdc++6, libgcc-s1" \
    "HavenOS Write application (LibreOffice Writer + LibreOfficeKit)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-write_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Present app
build_present() {
  local dir="$OUT/work/havenos-present"
  mkdir -p "$dir/usr/lib/havenos/present" "$dir/usr/bin"
  test -n "${PRESENT_APP_BUILD_DIR:-}" || { echo "PRESENT_APP_BUILD_DIR is required" >&2; exit 1; }
  test -x "$PRESENT_APP_BUILD_DIR/HavenOS.Present.App" || { echo "Present app binary is missing" >&2; exit 1; }
  cp -a "$PRESENT_APP_BUILD_DIR/." "$dir/usr/lib/havenos/present/"
  cat > "$dir/usr/bin/haven-present" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present/HavenOS.Present.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-present"
  if [[ -f "$PRESENT_APP_BUILD_DIR/HavenOS.Present.Hui" ]]; then
    cat > "$dir/usr/bin/haven-present-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present/HavenOS.Present.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-present-hui"
  fi
  write_control "$dir" havenos-present "havenos-present-engine, libreoffice-impress-nogui, libc6, libstdc++6, libgcc-s1" \
    "HavenOS Present application (LibreOffice Impress + LibreOfficeKit)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-present_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Plan app
build_plan() {
  local dir="$OUT/work/havenos-plan"
  mkdir -p "$dir/usr/lib/havenos/plan" "$dir/usr/bin"
  test -n "${PLAN_BUILD_DIR:-}" || { echo "PLAN_BUILD_DIR is required" >&2; exit 1; }
  test -x "$PLAN_BUILD_DIR/HavenOS.Plan.App" || { echo "Plan app binary is missing" >&2; exit 1; }
  cp -a "$PLAN_BUILD_DIR/." "$dir/usr/lib/havenos/plan/"
  cat > "$dir/usr/bin/haven-plan" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/plan/HavenOS.Plan.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-plan"
  if [[ -f "$PLAN_BUILD_DIR/HavenOS.Plan.Hui" ]]; then
    cat > "$dir/usr/bin/haven-plan-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/plan/HavenOS.Plan.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-plan-hui"
  fi
  write_control "$dir" havenos-plan "libc6, libstdc++6, libgcc-s1, libical3, libecal-2.0-1, libedataserver-1.2-26" \
    "HavenOS Plan application (Evolution Data Server + libical)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-plan_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Terminal app
build_terminal() {
  local dir="$OUT/work/havenos-terminal"
  mkdir -p "$dir/usr/lib/havenos/terminal" "$dir/usr/bin"
  test -n "${TERMINAL_BUILD_DIR:-}" || { echo "TERMINAL_BUILD_DIR is required" >&2; exit 1; }
  test -x "$TERMINAL_BUILD_DIR/HavenOS.Terminal.App" || { echo "Terminal app binary is missing" >&2; exit 1; }
  cp -a "$TERMINAL_BUILD_DIR/." "$dir/usr/lib/havenos/terminal/"
  cat > "$dir/usr/bin/haven-terminal" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/terminal/HavenOS.Terminal.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-terminal"
  if [[ -f "$TERMINAL_BUILD_DIR/HavenOS.Terminal.Hui" ]]; then
    cat > "$dir/usr/bin/haven-terminal-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/terminal/HavenOS.Terminal.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-terminal-hui"
  fi
  write_control "$dir" havenos-terminal "libc6, libstdc++6, libgcc-s1, libvterm0" \
    "HavenOS Terminal application (Linux PTY + libvterm)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-terminal_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Dev app
build_dev() {
  local dir="$OUT/work/havenos-dev"
  mkdir -p "$dir/usr/lib/havenos/dev" "$dir/usr/bin"
  test -n "${DEV_BUILD_DIR:-}" || { echo "DEV_BUILD_DIR is required" >&2; exit 1; }
  test -x "$DEV_BUILD_DIR/HavenOS.Dev.App" || { echo "Dev app binary is missing" >&2; exit 1; }
  cp -a "$DEV_BUILD_DIR/." "$dir/usr/lib/havenos/dev/"
  cat > "$dir/usr/bin/haven-dev" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/dev/HavenOS.Dev.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-dev"
  if [[ -f "$DEV_BUILD_DIR/HavenOS.Dev.Hui" ]]; then
    cat > "$dir/usr/bin/haven-dev-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/dev/HavenOS.Dev.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-dev-hui"
  fi
  write_control "$dir" havenos-dev "libc6, libstdc++6, libgcc-s1, codium" \
    "HavenOS Dev application (VSCodium/Code-OSS integration)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-dev_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Wave app
build_wave() {
  local dir="$OUT/work/havenos-wave"
  mkdir -p "$dir/usr/lib/havenos/wave" "$dir/usr/bin"
  test -n "${WAVE_BUILD_DIR:-}" || { echo "WAVE_BUILD_DIR is required" >&2; exit 1; }
  test -x "$WAVE_BUILD_DIR/HavenOS.Wave.App" || { echo "Wave app binary is missing" >&2; exit 1; }
  cp -a "$WAVE_BUILD_DIR/." "$dir/usr/lib/havenos/wave/"
  cat > "$dir/usr/bin/haven-wave" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wave/HavenOS.Wave.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-wave"
  if [[ -f "$WAVE_BUILD_DIR/HavenOS.Wave.Hui" ]]; then
    cat > "$dir/usr/bin/haven-wave-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wave/HavenOS.Wave.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-wave-hui"
  fi
  write_control "$dir" havenos-wave "libc6, libstdc++6, libgcc-s1, gstreamer1.0-plugins-base, gstreamer1.0-plugins-good, gstreamer1.0-plugins-bad, gstreamer1.0-libav, libges-1.0-0" \
    "HavenOS Wave application (GStreamer + GES)"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-wave_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Wine app
build_wine() {
  local dir="$OUT/work/havenos-wine"
  mkdir -p "$dir/usr/lib/havenos/wine" "$dir/usr/bin"
  test -n "${WINE_BUILD_DIR:-}" || { echo "WINE_BUILD_DIR is required" >&2; exit 1; }
  test -x "$WINE_BUILD_DIR/HavenOS.Wine.App" || { echo "Wine app binary is missing" >&2; exit 1; }
  cp -a "$WINE_BUILD_DIR/." "$dir/usr/lib/havenos/wine/"
  cat > "$dir/usr/bin/haven-wine" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wine/HavenOS.Wine.App "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-wine"
  if [[ -f "$WINE_BUILD_DIR/HavenOS.Wine.Hui" ]]; then
    cat > "$dir/usr/bin/haven-wine-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wine/HavenOS.Wine.Hui "$@"
EOF
    chmod 0755 "$dir/usr/bin/haven-wine-hui"
  fi
  write_control "$dir" havenos-wine "wine64, wine32:i386, libc6, libstdc++6, libgcc-s1" \
    "HavenOS Wine integration"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-wine_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build Wine compat (per-user Wine broker)
build_wine_compat() {
  local dir="$OUT/work/havenos-wine-compat"
  mkdir -p "$dir/usr/lib/havenos/compatibility" "$dir/usr/lib/systemd/user"
  cp -a "$ROOT/compatibility/wine" "$dir/usr/lib/havenos/compatibility/"
  sed -e 's|@PYTHON@|/usr/bin/python3|g' \
      -e 's|@SOURCE_ROOT@|/usr/lib/havenos/compatibility|g' \
      -e 's|@DOC_ROOT@|/usr/share/doc/havenos-wine-compat|g' \
      "$ROOT/compatibility/wine/systemd/haven-compatd.service.in" \
      > "$dir/usr/lib/systemd/user/haven-compatd.service"
  write_control "$dir" havenos-wine-compat "python3, bubblewrap, systemd" \
    "HavenOS per-user Wine compatibility broker"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-wine-compat_${VERSION}_${ARCH}.deb" >/dev/null
}

# Build all packages
build_data
build_present_engine
build_write
build_present
build_plan
build_terminal
build_dev
build_wave
build_wine
build_wine_compat

# Generate metadata
for deb in "$OUT"/*.deb; do
  sha256sum "$deb"
done > "$OUT/SHA256SUMS"
: > "$OUT/package-control.txt"
for deb in "$OUT"/*.deb; do
  dpkg-deb --info "$deb" >> "$OUT/package-control.txt"
done
cat > "$OUT/cohort.json" <<EOF
{
  "version": "$VERSION",
  "architecture": "$ARCH",
  "packages": [
    {"name": "havenos-data", "artifact": "$(basename "$OUT/havenos-data_${VERSION}_${ARCH}.deb")", "depends": ["python3", "python3-duckdb", "python3-uno", "libreoffice-calc-nogui", "libc6", "libstdc++6", "libgcc-s1"]},
    {"name": "havenos-present-engine", "artifact": "$(basename "$OUT/havenos-present-engine_${VERSION}_${ARCH}.deb")", "depends": ["libreoffice-impress-nogui", "libstdc++6", "libc6", "libglib2.0-0"]},
    {"name": "havenos-write", "artifact": "$(basename "$OUT/havenos-write_${VERSION}_${ARCH}.deb")", "depends": ["havenos-present-engine", "libreoffice-writer-nogui", "libc6", "libstdc++6", "libgcc-s1"]},
    {"name": "havenos-present", "artifact": "$(basename "$OUT/havenos-present_${VERSION}_${ARCH}.deb")", "depends": ["havenos-present-engine", "libreoffice-impress-nogui", "libc6", "libstdc++6", "libgcc-s1"]},
    {"name": "havenos-plan", "artifact": "$(basename "$OUT/havenos-plan_${VERSION}_${ARCH}.deb")", "depends": ["libc6", "libstdc++6", "libgcc-s1", "libical3", "libecal-2.0-1", "libedataserver-1.2-26"]},
    {"name": "havenos-terminal", "artifact": "$(basename "$OUT/havenos-terminal_${VERSION}_${ARCH}.deb")", "depends": ["libc6", "libstdc++6", "libgcc-s1", "libvterm0"]},
    {"name": "havenos-dev", "artifact": "$(basename "$OUT/havenos-dev_${VERSION}_${ARCH}.deb")", "depends": ["libc6", "libstdc++6", "libgcc-s1", "codium"]},
    {"name": "havenos-wave", "artifact": "$(basename "$OUT/havenos-wave_${VERSION}_${ARCH}.deb")", "depends": ["libc6", "libstdc++6", "libgcc-s1", "gstreamer1.0-plugins-base", "gstreamer1.0-plugins-good", "gstreamer1.0-plugins-bad", "gstreamer1.0-libav", "libges-1.0-0"]},
    {"name": "havenos-wine", "artifact": "$(basename "$OUT/havenos-wine_${VERSION}_${ARCH}.deb")", "depends": ["wine64", "wine32:i386", "libc6", "libstdc++6", "libgcc-s1"]},
    {"name": "havenos-wine-compat", "artifact": "$(basename "$OUT/havenos-wine-compat_${VERSION}_${ARCH}.deb")", "depends": ["python3", "bubblewrap", "systemd"]}
  ]
}
EOF
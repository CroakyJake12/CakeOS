# Component release evidence

This record identifies the authoritative source trees materialized in this
checkout. It does not claim that a Debian package or installed VM artifact
exists when the source branch does not provide one.

## Data

- **Source:** `apps/Data/` from `e3a195b032bd57b4bf85ff82ff8f6a5e7166d1c6`
  (`data-calc-duckdb-first-slice`).
- **Dependencies:** .NET SDK, Python 3, LibreOffice Calc headless with
  `libreoffice-calc-nogui`, `libreoffice-core-nogui`,
  `libreoffice-common`, `python3-uno`, and DuckDB 1.5.5 as described by
  `apps/Data/README.md`.
- **Launcher:** none supplied by the authoritative tree. The C# project and
  worker entry points are not desktop launchers.
- **Package:** no package recipe or package artifact is supplied; package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** `dotnet run --project
  apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release`, the Python worker
  tests, and the runtime projects listed in `apps/Data/README.md`.
- **Offline:** the source documentation reports disposable Ubuntu runtime
  proof, not approved CakeOS VM or offline application proof.

## Present

- **Source:** `apps/present-engine/` from
  `b37a1e1a9ab9cbd03a9269438f3e15acb6921e65`
  (`migration/present-libreoffice-foundation`).
- **Dependencies:** CMake 3.20+, C++20 compiler, `libreofficekit-dev`,
  `nlohmann-json3-dev`, and a no-GUI LibreOffice Impress/Draw runtime.
- **Launcher:** none supplied. `cakeos-present-worker` is an out-of-process
  protocol worker, not a desktop launcher.
- **Package:** no package recipe or package artifact is supplied; package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** configure and build with the CMake project, run CTest,
  and use the worker protocol smoke tests described in
  `apps/present-engine/README.md`.
- **Offline:** runtime proof is documented for disposable Ubuntu CI only;
  approved VM and functional offline application status are **NOT PROVEN**.

## Wine compatibility

- **Source:** `compatibility/wine/` from
  `4e513d27ca6099ab7351c3d528256fdc0e26b4a1`
  (`feature/windows-compat-broker`).
- **Dependencies:** Python 3, Bubblewrap, per-user systemd
  (`systemctl`, `systemd-run`, `journalctl`), Wayland, and a separately
  managed versioned Wine runtime. The broker deliberately does not install or
  bundle Wine.
- **Launcher:** no desktop launcher is supplied. The supported entry points
  are the CLI and the rendered user service template
  `compatibility/wine/systemd/haven-compatd.service.in`.
- **Package:** `compatibility/wine/PACKAGING.md` is a packaging contract only;
  no package recipe or package artifact is supplied. Package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** `compatibility/wine/acceptance.sh` renders and verifies
  the user unit and runs the read-only prerequisite audit; it does not install
  services, create prefixes, or launch Windows applications.
- **Application launch/offline:** no Windows application compatibility or
  offline functional claim is made by this slice.

## Write recovery

A bounded check of `git log --all --oneline -- apps/write apps/Write
apps/present apps/data compatibility/wine` and
`git ls-tree -r --name-only origin/main -- apps/write apps/Write apps/present
apps/data` found no Write source, package, launcher, or handoff artifact.
**BLOCKED — NO AUTHORITATIVE IMPLEMENTATION RECOVERED.** Write is not
inferred from unrelated components.

## Ubuntu cohort packaging

`.github/workflows/cohort-packages.yml` runs on Ubuntu 24.04 and invokes
`packaging/cohort/build-debs.sh`. It builds:

- `havenos-data_<version>_amd64.deb`, launcher
  `/usr/bin/haven-data-calc-worker`;
- `havenos-present_<version>_amd64.deb`, launcher
  `/usr/bin/cakeos-present-worker`;
- `havenos-wine-compat_<version>_amd64.deb`, user service
  `/usr/lib/systemd/user/haven-compatd.service`.

The workflow records per-package SHA-256 values in `SHA256SUMS`, dependency
metadata in `cohort.json`, and the GitHub artifact ID/digest in the release
record. `packaging/cohort/verify-debs.sh` extracts each package into a clean
root, checks installed paths, verifies the rendered Wine unit has no
placeholders, runs Python syntax smoke checks, and verifies checksums.

## Canvas (Rnote PoC)

- **Source:** `apps/canvas/rnote-poc/` from `d30b23fbafba7199f8b182f8a84cc560620a7d053847c9c7a56ef37de6049db8`
  (immutable artifact pinned to Rnote 0.14.2 headless engine).
- **Native Library:** `libcakeos_canvas_rnote_poc.so` (C ABI v1, renderer-neutral SVG + document-space bounds).
- **Dependencies:** Rust 1.92+, `rnote-engine` 0.14.2 (no `ui` feature), `rnote-compose` 0.14.2, `nalgebra`, `anyhow`, `futures`.
- **HUI Bridge:** `CakeOS.HuiLinuxHost.Canvas.CanvasNativeSession` (P/Invoke → `cake_canvas_*` symbols).
- **Managed Proof:** `CanvasManagedBoundaryProof.Run()` — pen/eraser strokes, undo/redo, SVG render frame, `.rnote` save/reload round-trip with bounds verification.
- **Launcher:** `cakeos-canvas-rnote` (HUI Linux host with `CAKEOS_HUI_CANVAS_PREVIEW=1`).
- **Package:** `packaging/canvas-rnote/build-deb.sh` → `cakeos-canvas-rnote_0.1.0_amd64.deb` (installs to `/usr/lib/cakeos/canvas-rnote/`, `/usr/bin/cakeos-canvas-rnote`).
- **Smoke Contract:** 
  - Native: `cargo test --all-targets` (Rust FFI + engine tests)
  - Managed: `CAKEOS_HUI_CANVAS_PREVIEW=1 CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS=5000 dotnet run --project HUI/LinuxHost/CakeOS.HuiLinuxHost.csproj`
- **VM Validation Gate:** Install `.deb` → launch `cakeos-canvas-rnote` → verify CakeUI toolstrip (Pen/Eraser/Undo/Redo) → draw stroke → verify SVG render → save `.rnote` → reopen → verify identical bounds → exit → verify launcher registration in app grid.

## Boards (AppFlowy Integration)

- **Source:** `apps/Boards/` — contract from `22ed955b2647b78c2daf370c6e26fcc6d0db4b2e2127b566c3638e76dc26cff8`, HUI frontend implemented in this phase.
- **Contract:** `CakeOS.Apps.Boards.Contract` — `HavenBoardSnapshot`, `HavenBoardCommand` (CreateCard, MoveCard, MoveGroup, RenameGroup, SetFreeformFrame, Attachments), `HavenBoardReducer` (pure reducer with validation).
- **HUI Frontend:** `apps/Boards/hui/` — `BoardsApp` (Avalonia/HUI), `BoardsMainPage` (toolbar + scrollable board surface), `BoardsViewModel` (MVVM, command sink), `FileSystemBoardStore` (XDG versioned settings persistence).
- **Dependencies:** .NET 10, Avalonia 11.2, `CakeOS.Platform`, `CakeOS.HuiLinuxHost`.
- **Launcher:** `cakeos-boards` (HUI Linux host).
- **Package:** `packaging/boards-appflowy/build-deb.sh` → `cakeos-boards_0.1.0_amd64.deb` (installs to `/usr/lib/cakeos/boards/`, `/usr/bin/cakeos-boards`).
- **Smoke Contract:** 
  - Contract: `dotnet test apps/Boards/hui-tests/CakeOS.Apps.Boards.Hui.Tests.csproj`
  - Managed: `CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS=5000 dotnet run --project apps/Boards/hui/CakeOS.Apps.Boards.Hui.csproj`
- **VM Validation Gate:** Install `.deb` → launch `cakeos-boards` → verify board renders with 3 groups (To do/Doing/Done) → add card → move card between groups → move group → reorder → close → reopen → verify persistence → verify launcher registration.

## Images (glycin/libglycin Backend + CakeUI Frontend)

- **Architecture:** Per `docs/loupe-cakeui-images-adaptation-architecture.md` — strict backend/frontend separation.
- **Backend:** `apps/images/backend/` — `cakeos-images-backend` (Rust, `glycin >= 2.0`, `lcms2`, `cairo-rs`).
  - C ABI v1: `cake_images_decode`, `render`, `metadata_read`, `thumbnail`, `transform_apply`, `color_profile_apply`.
  - Formats: JPEG, PNG, WebP, AVIF, HEIC, TIFF, BMP, ICO, SVG (via librsvg).
  - Color management: ICC v2/v4, display calibration, soft-proof.
  - Output: `libcakeos_images_backend.so` → packaged as `cakeos-images-backend`.
- **Interop:** `apps/images/interop/` — `LoupeBackendInterop` (C# P/Invoke bindings matching backend ABI).
- **Frontend:** `apps/images/frontend/` — `cakeos-images` (Avalonia/HUI).
  - `ImagesApp` + `ImagesMainPage` (toolbar, Cairo-backed viewport, filmstrip).
  - `ImagesViewModel` (zoom/pan/rotate, AI Vision handoff, metadata panel).
  - `IAiVisionClient` (gRPC stub for CakeAI Vision: objects, text, caption, tags).
  - `IPersistenceLayer` + `SidecarPersistenceLayer` (XMP sidecar for annotations + AI tags).
- **Dependencies:** .NET 10, Avalonia 11.2, `CakeOS.Platform`, `CakeOS.HuiLinuxHost`, `Grpc.Net.Client`, `Google.Protobuf`.
- **Launcher:** `cakeos-images` (HUI Linux host with `CAKEOS_HUI_IMAGES_PREVIEW=1`).
- **Package:** `packaging/images-glycin/build-deb.sh` → `cakeos-images-backend_0.1.0_amd64.deb` + `cakeos-images_0.1.0_amd64.deb`.
- **Smoke Contract:**
  - Backend: `cargo test --all-targets` (decode, render, thumbnail, transform, color profile)
  - Managed: `CAKEOS_HUI_IMAGES_PREVIEW=1 CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS=5000 dotnet run --project apps/images/frontend/CakeOS.Images.Frontend.csproj`
- **VM Validation Gate:** Install both `.deb` → launch `cakeos-images` → open test image → verify decode → zoom/pan cursor-anchored → rotate → AI Analyze (mock) → verify tags persisted → save annotations → close → reopen → verify annotations pixel-identical → verify launcher registration.

## Phase 2B Acceptance Summary

| App | Artifact SHA | Package | Install Path | Launcher | VM Gate |
|-----|-------------|---------|--------------|----------|---------|
| Canvas | d30b23fb... | cakeos-canvas-rnote | /usr/lib/cakeos/canvas-rnote/ | cakeos-canvas-rnote | Graphical: draw→save→reload→bounds |
| Boards | 22ed955b... | cakeos-boards | /usr/lib/cakeos/boards/ | cakeos-boards | Admission: CRUD→persist→reopen |
| Images | (new) | cakeos-images-backend + cakeos-images | /usr/lib/cakeos/images-backend/ + /usr/lib/cakeos/images/ | cakeos-images | Decode→zoom→AI→annotate→round-trip |

**Next Gates:**
1. Backend CI — Build `cakeos-images-backend` + `cakeos-images` Debian packages, publish to cohort.
2. ISO Inclusion — Add both packages to `havenos.list.chroot`, boot VM, launch from app grid.
3. Windows CI — Portable build with WIC fallback, shell extension test.

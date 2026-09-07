# Canvas / Rnote / HUI integration evidence

This document records the first graphical integration proof for the Canvas migration. It is evidence, not a claim that the production migration is complete.

## Branch isolation

- Integration branch: `integration/canvas-rnote-hui-preview`.
- HUI baseline: stacked from `platform/hui-linux-graphical-preview` without modifying that worker branch.
- Rnote bridge: stacked from `migration/canvas-rnote-poc`.
- `main` was not merged or modified by this integration proof.
- The approved CakeOS VM was not modified.

## Native engine boundary

- Rnote release: `v0.14.2`.
- Consumed Rust packages: `rnote-compose` and `rnote-engine` with default/UI features disabled.
- Native bridge format: Rust `cdylib` with C ABI version 1.
- HUI-facing pointer data: document-space X/Y, pressure and retained X/Y tilt.
- Proven tools: Pen and Eraser.
- Proven engine operations: incremental begin/update/end stroke, undo/redo, SVG render frame, `.rnote` save/reload.
- Rnote GTK/Libadwaita UI is not linked into the bridge. CI rejects GTK4/Libadwaita in both the Cargo dependency graph and `ldd` output.

## Managed HUI boundary

The Linux HUI preview is .NET 10 / Avalonia. `CanvasNativeSession` loads the native ABI by library name and validates ABI version 1 before creating an engine. Rnote renderer output remains whole-document SVG in Canvas document coordinates.

HUI owns device/input arbitration and surface-to-document mapping. The first graphical bridge accepts mouse and pen ink; touch is intentionally not converted to ink yet so it remains available for the future HUI pan/zoom gesture layer. Avalonia pen pressure and X/Y tilt are passed through the managed boundary; Rnote 0.14.2 consumes pressure but not tilt, so tilt remains a CakeOS document/API concern.

The HUI scene uses the platform-neutral HUI `Image` component with the internal source token `cake-canvas://rnote-frame`. The Linux backend resolves only that Canvas token to an in-memory SVG image. No temporary document file is required for rendering.

The Linux preview adds `Svg.Controls.Skia.Avalonia` `12.0.0.17` as the SVG decoder. Its upstream Svg.Skia project is MIT licensed. This does not change Rnote's GPL-3.0-or-later compliance obligations.

## Runtime proof

Workflow: `Canvas Rnote HUI graphical preview`

Green pointer-interaction run:

- Run ID: `34148065189`
- Head SHA: `ace45d710607a8ed28ef585109019066415a3fa4`
- Runner: Ubuntu 24.04 GitHub Actions
- Display: X11 under Xvfb + Openbox
- Result: success
- Evidence artifact: `canvas-rnote-hui-graphical-preview`
- Artifact digest: `sha256:5d23b94ee34407d45cc5d243bfa218e5fc005b77b56f49e4d4889d3e9be5473f`

The gate mechanically proved:

1. pinned HUI donor staging succeeds;
2. Rnote core dependency graph contains no GTK4/Libadwaita;
3. the native Canvas `.so` builds and all Rust/FFI tests pass;
4. native `ldd` closure resolves and contains no GTK/Libadwaita;
5. the .NET HUI Linux host builds with the managed Canvas adapter;
6. the HUI host native closure resolves;
7. the managed host loads ABI version 1 and obtains/decodes a Rnote SVG frame;
8. HUI pointer and keyboard self-test passes;
9. a real X11 mouse drag is delivered through Avalonia/HUI, mapped to Rnote document coordinates, committed as a new Rnote stroke, and re-rendered through the HUI image command;
10. the application exits 0 and a 960x600 window screenshot is captured.

Runtime log markers from the green run include:

```text
CANVAS_RNOTE_HUI_RENDER_READY abi=1 format=svg coordinate=document width=9784 height=13296
CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer=Mouse pressure=0.5 width=9784 height=13485.171
```

The captured screenshot was manually inspected and visibly contains both the seeded Rnote stroke and the pointer-created stroke, with the HUI status reporting that pointer ink was committed to Rnote and undo is available.

## Evidence classification

- Rnote source/API: **inspected**.
- Headless Rnote core: **built and tested**.
- Native C ABI `.so`: **built and tested**.
- Managed .NET native adapter: **built and runtime-loaded**.
- HUI image rendering of Rnote output: **graphically runtime-proven under Xvfb/X11**.
- Mouse ink through HUI into Rnote: **runtime-proven under Xvfb/X11**.
- Synthetic pressure path: **tested**.
- Real stylus pressure/tilt hardware: **not runtime-proven**.
- Touch/palm rejection: **not implemented/runtime-proven**.
- HUI pan/zoom gesture layer: **not yet implemented/runtime-proven**.
- Dirty-region/tile renderer: **not implemented**; current contract renders whole-document SVG.
- Wayland runtime: **not runtime-proven**.
- Approved CakeOS VM: **not modified or runtime-proven**.
- CakeOS package/image integration: **not implemented**.
- Boot validation: **not attempted**.

## Next acceptance gate

Keep viewport state in HUI rather than moving it into Rnote. Add zoom/pan and source-region composition around the renderer-neutral frame, then validate real Wayland pen/touch behaviour before considering dirty-region/tile rendering or image/package integration.

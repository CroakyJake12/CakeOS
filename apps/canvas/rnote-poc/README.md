# CakeOS Canvas / Rnote PoC

Renderer-neutral Rnote 0.14.2 integration for CakeOS HUI.

## Architecture

```
HUI (Avalonia/.NET)  ── P/Invoke ──▶  libcakeos_canvas_rnote_poc.so  ──▶  Rnote Engine (Rust)
       │                                                       │
       │                    Renderer-neutral SVG               │
       │                  + document-space bounds              │
       ▼                                                       ▼
CanvasNativeSession                              HeadlessCanvasEngine
```

## Components

- `src/lib.rs` - Headless engine, render frame, save/load
- `src/ffi.rs` - C ABI exports (`cake_canvas_*`)
- `include/cakeos_canvas.h` - C header for consumers
- `Cargo.toml` - Dependencies (Rnote engine/compose, no UI features)

## Verified Capabilities

| Capability | Status |
|------------|--------|
| Pen strokes (pressure + tilt) | ✅ |
| Eraser strokes | ✅ |
| Undo/Redo | ✅ |
| SVG render frame (doc-space bounds) | ✅ |
| `.rnote` save/reload round-trip | ✅ |
| No GTK/Libadwaita linkage | ✅ |

## Build

```bash
cargo build --lib --release
```

## Test

```bash
cargo test --all-targets
```

## Integration

The HUI Linux host (`CakeOS.HuiLinuxHost`) loads the native library by name and uses
`CanvasNativeSession` as the managed adapter. The HUI scene renders the SVG frame
via `Svg.Controls.Skia.Avalonia`.
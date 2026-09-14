# Canvas/Rnote PoC Provenance

This crate provides the authoritative native bridge between the CakeOS HUI Linux host and the Rnote 0.14.2 headless engine.

## Source

- Rnote upstream: `https://github.com/flxzt/rnote`
- Pinned tag: `v0.14.2`
- Consumed packages (default features disabled):
  - `rnote-compose`
  - `rnote-engine`

## ABI Contract

- Library name: `cakeos_canvas_rnote_poc`
- ABI version: 1
- C calling convention
- Opaque engine handle (`void*`)
- Renderer-neutral SVG output with document-space coordinate metadata
- Native `.rnote` payload save/restore round-trip

## Verified Operations

- Incremental stroke lifecycle (begin/update/end) with pressure and tilt
- Pen and Eraser tool switching (outside active stroke)
- Undo/redo with stroke-activity guard
- SVG render frame with coordinate contract
- `.rnote` save/reload producing bit-identical render bounds
- No GTK4/Libadwaita in dependency closure or `ldd` output

## Build

```bash
cargo build --lib --release
# Produces target/release/libcakeos_canvas_rnote_poc.so
```

## Integration

The HUI Linux host loads this library by name (`cakeos_canvas_rnote_poc`) via P/Invoke.
The managed adapter is `CakeOS.HuiLinuxHost.Canvas.CanvasNativeSession`.

## Licence

GPL-3.0-or-later (inherited from Rnote).
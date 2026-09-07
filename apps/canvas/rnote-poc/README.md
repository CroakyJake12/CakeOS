# CakeOS Canvas Rnote proof of concept

This is the smallest approved implementation slice from the Canvas migration feasibility dossier.

## Scope

The crate pins upstream Rnote `v0.14.2` and consumes only `rnote-compose` and `rnote-engine` with default features disabled. It does **not** embed or depend on `rnote-ui`, GTK4, Libadwaita, GNOME Shell, or GSK.

The proof now exercises:

1. instantiate a headless Rnote `Engine`;
2. accept an incremental HUI-neutral `begin_stroke` / `update_stroke` / `end_stroke` lifecycle;
3. feed normalized pressure-sensitive document-space samples through Rnote's abstract pen-event API;
4. preserve tilt in the CakeOS boundary even though Rnote 0.14.2 does not consume it;
5. export renderer-neutral whole-document SVG bytes;
6. save native `.rnote` bytes;
7. load those bytes into a fresh engine and export again;
8. mechanically assert that the dependency tree does not contain GTK4 or Libadwaita.

## HUI boundary

`CanvasPointerSample.x` and `.y` are **Canvas document-space coordinates**, not widget/surface coordinates. HUI owns device arbitration, palm rejection, touch-versus-pen gesture policy, surface-to-document coordinate transforms, and the visible viewport pan/zoom state. The Rnote adapter owns only the engine-side pen lifecycle and pressure mapping.

This is intentional. Rnote's own GTK UI converts surface coordinates through its camera transform before it creates pen elements. Keeping the stable CakeOS adapter document-space-first prevents HUI and Rnote from developing two competing viewport/camera states.

For the first HUI-visible slice, the adapter's rendering contract is whole-document SVG and HUI is responsible for viewport composition. Rnote's internal camera and private render-component machinery are not part of the stable bridge. Dirty-region/tile rendering is a later performance gate and may require a narrow upstream patch or fork rather than exposing GTK/GSK types.

Tilt stays in `CanvasPointerSample` because Rnote 0.14.2's core pen element stores position and pressure only. The adapter must not erase tilt from CakeOS documents even when the current engine cannot use it for brush rendering.

## Toolchain and dependency resolution

The CakeOS PoC declares Rust `1.92` and CI validates with Rust 1.92.

Rnote `v0.14.2` declares a workspace Rust version of 1.89, but its tagged dependency lock includes `vello_api`, `vello_common`, and `vello_cpu` 0.0.7. The current crates require Rust 1.92, so a freshly resolved CakeOS PoC does not build with Rust 1.89. The PoC therefore records the actually tested 1.92 floor rather than claiming upstream's declared 1.89 floor is sufficient.

A resolved `Cargo.lock` is generated on every CI proof and preserved in the CI evidence artifact. It is **not yet committed to this branch**, so local commands must not use `--locked` until that exact tested lockfile is committed through a host-safe repository path.

## Build and test

On an Ubuntu host with the native Rnote core build dependencies installed:

```sh
cargo generate-lockfile --manifest-path apps/canvas/rnote-poc/Cargo.toml
cargo tree --manifest-path apps/canvas/rnote-poc/Cargo.toml
cargo test --manifest-path apps/canvas/rnote-poc/Cargo.toml --all-targets
```

The repository CI workflow performs the same proof on Ubuntu, rejects a dependency graph containing `gtk4` or `libadwaita`, captures test diagnostics, and preserves the generated lockfile as an artifact.

## Evidence labels

- Source integrated: **yes** on `migration/canvas-rnote-poc`.
- Upstream copied/forked: **no**. Rnote is referenced as a pinned Git dependency.
- Built: **yes** on Ubuntu GitHub Actions with Rust 1.92.
- Tested: **yes**. The headless pressure stroke, incremental lifecycle, SVG export, `.rnote` save/reload, post-reload export, and no-GTK/Libadwaita gate are green.
- Last code proof: workflow run `34145778560` for commit `391b7dde74cb8c4fa8bbab44e41dd7c99407d963`.
- HUI-rendered: **no**. SVG is the renderer-neutral boundary proof, not a HUI surface.
- Runtime pen/touch proven: **no**. The tests use synthetic pointer events.
- Wayland/tablet hardware proven: **no**.
- Packaged into the CakeOS image: **no**.
- Approved VM modified or runtime-proven: **no**.

## Next gate

The next source slice is a small renderer-neutral frame contract around the SVG output, including explicit document-coordinate metadata, followed by the first HUI Canvas surface when the HUI runtime scaffold exists. After that, dirty-region rendering should be benchmarked before multi-board, connectors, generative UI, or real tablet integration are migrated.

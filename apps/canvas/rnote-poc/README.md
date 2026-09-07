# CakeOS Canvas Rnote proof of concept

This is the smallest approved implementation slice from the Canvas migration feasibility dossier.

## Scope

The crate pins upstream Rnote `v0.14.2` and consumes only `rnote-compose` and `rnote-engine` with default features disabled. It does **not** embed or depend on `rnote-ui`, GTK4, Libadwaita, GNOME Shell, or GSK.

The proof exercises:

1. instantiate a headless Rnote `Engine`;
2. feed normalized pressure-sensitive pointer samples through Rnote's abstract pen-event API;
3. export renderer-neutral SVG bytes;
4. save native `.rnote` bytes;
5. load those bytes into a fresh engine;
6. export again after reload;
7. mechanically assert that the dependency tree does not contain GTK4 or Libadwaita.

Haven/CakeOS keeps tilt in its own `CanvasPointerSample` boundary because Rnote 0.14.2's core pen element stores position and pressure only. This PoC must not erase that information from the future HUI contract.

## Build and test

On an Ubuntu host with the native Rnote core build dependencies installed:

```sh
cargo test --manifest-path apps/canvas/rnote-poc/Cargo.toml --locked
cargo tree --manifest-path apps/canvas/rnote-poc/Cargo.toml
```

The repository CI workflow performs the same proof on Ubuntu and rejects a dependency graph containing `gtk4` or `libadwaita`.

## Evidence labels

- Source integrated: yes, this CakeOS adapter source exists on its migration branch.
- Upstream copied/forked: no. Rnote is referenced as a pinned Git dependency.
- Built: only after a successful CI/local build is recorded.
- Tested: only after the test job succeeds.
- HUI-rendered: no; SVG is the renderer-neutral boundary proof, not a HUI surface.
- Runtime pen/touch proven: no; this test uses synthetic pointer events.

## Next gate

After this core round trip is green, the next implementation slice is a stable bridge API that exposes dirty render output to HUI without enabling Rnote's `ui` feature. That bridge should be benchmarked before multi-board, connectors, generative UI, or hardware tablet integration are migrated.

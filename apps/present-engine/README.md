# CakeOS Present engine foundation

This directory contains the first implementation slice for migrating Present onto a LibreOffice Impress document engine while keeping HUI as the only application UI.

## Boundary

```text
HUI / Present semantic commands
          |
          v
cakeos::present::PresentEngine
          |
          v
LibreOfficeKit tiled editing/rendering
          |
          v
LibreOffice Impress/Draw no-GUI runtime
```

The public header deliberately exposes no LibreOffice objects, GTK widgets, GNOME Shell APIs, or UNO types. LibreOfficeKit is an implementation detail. This lets HUI evolve independently and gives CakeOS one place to absorb LibreOfficeKit API/version changes.

The current slice supports:

- opening presentation documents through LibreOfficeKit;
- rejecting non-presentation documents;
- slide enumeration and slide selection;
- querying the logical presentation extent;
- rendering a slide region into a four-channel pixel buffer;
- keyboard and mouse event forwarding;
- `.uno:` semantic command forwarding;
- save/export through LibreOffice filters;
- forwarding LibreOfficeKit callbacks to the eventual HUI adapter;
- a private LibreOffice user profile by default so Present does not share the user's desktop LibreOffice profile.

It does **not** yet claim:

- a HUI Present application surface;
- full semantic mapping of the legacy Present document model;
- presenter-mode animations/transitions;
- media playback;
- a complete accessibility semantic tree;
- PPTX fidelity parity with Microsoft PowerPoint;
- production sandboxing/service supervision.

Those are later gates rather than assumptions hidden in this foundation.

## Linux dependencies

The intended distro runtime is the no-GUI LibreOffice stack. On Ubuntu this is provided by `libreoffice-impress-nogui` and its Draw/core dependencies. Development additionally requires `libreofficekit-dev`.

No GTK embedding is used by this code.

## Build

```sh
cmake -S apps/present-engine -B build/present-engine -DCMAKE_BUILD_TYPE=Release
cmake --build build/present-engine --parallel
ctest --test-dir build/present-engine --output-on-failure
```

## Runtime smoke proof

Create the deterministic one-slide ODP test deck and render it:

```sh
python3 apps/present-engine/tests/make_smoke_fixture.py /tmp/cakeos-present-smoke.odp
./build/present-engine/cakeos-present-engine-smoke \
  /tmp/cakeos-present-smoke.odp \
  /tmp/cakeos-present-smoke.ppm
```

A successful run proves only this slice: LibreOfficeKit initialization, Impress document load, slide enumeration, logical extent retrieval, and tiled rendering. It does not prove the full Present migration.

## HUI/AI integration direction

The migration donor already models Present AI changes as semantic operations such as add slide, add text, set title, add shape/image/media, and remove element. The HUI adapter should retain that pattern and translate semantic operations into this engine boundary / UNO commands rather than automating LibreOffice UI coordinates.

## Version strategy

Tiled LibreOfficeKit APIs require `LOK_USE_UNSTABLE_API`, so HUI must never depend on the LOK ABI directly. Advanced slideshow-layer APIs available in newer LibreOffice releases will be added behind a version-gated adapter after the baseline tiled engine is proven on the target CakeOS runtime.

## Evidence labels

- **Inspected**: source/API/package metadata reviewed.
- **Built**: this directory compiles in the named environment.
- **Tested**: contract tests execute successfully in the named environment.
- **Runtime-proven**: the smoke deck is opened and rendered through a real LibreOfficeKit runtime.

The CI workflow is the authoritative automated evidence for build/test/runtime smoke on its pinned Ubuntu runner. The approved CakeOS VM remains a separate acceptance gate and must not be described as runtime-proven until the same test is executed there.

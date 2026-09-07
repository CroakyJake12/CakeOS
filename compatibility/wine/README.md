# HavenOS Windows compatibility broker

This directory contains the first implementation slice of the HavenOS optional Windows compatibility layer.

## Implemented

- provider-based manifest model (`wine` and reserved `winboat` provider names)
- per-application Wine state/prefix locations
- fail-closed Bubblewrap requirement
- private network namespace by default
- explicit host mount grants with broad `/`, `/home`, and `/root` mounts rejected
- explicit GPU render-node grant
- Wayland-only display socket exposure for slice 1
- explicit refusal of clipboard/media permissions until enforceable mediation exists
- explicit refusal of the WinBoat provider until its VM/container boundary is implemented and proven
- reset/delete boundary restricted to the broker state root

## Not implemented or claimed

- Wine installation or downloading
- a bundled Wine runtime
- WinBoat, Podman, Docker, QEMU/KVM, Windows installation, or FreeRDP orchestration
- X11 fallback
- PipeWire output-only mediation
- clipboard brokering
- USB/smartcard/camera/microphone passthrough
- application compatibility claims
- approved-VM runtime proof

## Manifest example

```json
{
  "id": "example.app",
  "backend": "wine",
  "runtime": "wine-11.0",
  "entrypoint": "C:\\Program Files\\Example\\Example.exe",
  "network": "none",
  "clipboard": false,
  "audioOutput": false,
  "microphone": false,
  "gpu": "none",
  "mounts": []
}
```

Network values are `none`, `internet`, or `lan`. Slice 1 treats both network-enabled values as an explicit request to share host networking; finer Internet-vs-LAN enforcement remains a future backend requirement and must not be claimed until implemented.

## Runtime layout

The broker expects versioned Wine runtimes beneath:

`$XDG_DATA_HOME/haven/compat/wine/runtimes/<runtime>/bin/wine`

and stores application state beneath:

`$XDG_DATA_HOME/haven/compat/wine/apps/<app-id>/`

The broker never installs a missing runtime and never falls back to an unsandboxed system Wine executable.

## Tests

The test suite is dependency-free:

```sh
python3 -m unittest tests.test_haven_compat -v
```

Tests validate policy generation only. Passing tests are not runtime proof that Wine applications work on the approved HavenOS VM.

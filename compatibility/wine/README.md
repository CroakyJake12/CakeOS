# CakeOS Windows compatibility broker

This directory contains the first implementation slice of the optional Windows compatibility layer originally planned under the HavenOS name.

## Implemented

- provider-based manifest model (`wine` and reserved `winboat` provider names)
- per-application Wine state/prefix locations
- managed runtime identifiers that cannot traverse outside the runtime root
- fail-closed Bubblewrap requirement
- private network namespace with all network grants refused in slice 1
- explicit host mount grants restricted to `/mnt/haven-share/<name>` inside the sandbox
- host mount sources resolved before launch, with sensitive system/backend paths refused
- optional GPU render-node grant that fails if no render node is available
- Wayland-only display socket exposure for slice 1
- explicit refusal of clipboard/media permissions until enforceable mediation exists
- explicit refusal of the WinBoat provider until its VM/container boundary is implemented and proven
- reset/delete boundary restricted to the broker state root
- read-only runtime audit for Wine, Bubblewrap, Wayland, KVM, GPU, PipeWire, Podman/Docker, and FreeRDP prerequisites

## Not implemented or claimed

- Wine installation or downloading
- a bundled Wine runtime
- network mediation or Internet-versus-LAN separation
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
  "mounts": [
    {
      "source": "/home/user/Documents/Example",
      "target": "/mnt/haven-share/example",
      "mode": "ro"
    }
  ]
}
```

The manifest parser reserves `internet` and `lan` for future policy versions, but slice 1 refuses both at launch. This prevents a misleading permission label from silently becoming unrestricted host networking.

## Runtime layout

The broker expects versioned Wine runtimes beneath:

`$XDG_DATA_HOME/haven/compat/wine/runtimes/<runtime>/bin/wine`

and stores application state beneath:

`$XDG_DATA_HOME/haven/compat/wine/apps/<app-id>/`

The broker never installs a missing runtime and never falls back to an unsandboxed system Wine executable.

## Read-only runtime audit

Run the audit on the target Linux session without changing packages, VM settings, or containers:

```sh
python3 -m compatibility.wine.haven_compat.cli audit
```

The JSON output reports prerequisite presence separately for `wineSlice1` and `winboatFuture`. `prerequisitesPresent: true` is only a preflight result; it is not evidence that a Windows application was launched successfully.

## Broker commands

```sh
python3 -m compatibility.wine.haven_compat.cli plan manifest.json
python3 -m compatibility.wine.haven_compat.cli launch manifest.json
python3 -m compatibility.wine.haven_compat.cli reset manifest.json
```

## Tests

The test suite is dependency-free:

```sh
python3 -m unittest tests.test_haven_compat -v
```

Tests validate policy generation and prerequisite evaluation only. Passing tests are not runtime proof that Wine applications work on the approved CakeOS/HavenOS development VM.

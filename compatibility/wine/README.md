# HavenOS Windows compatibility broker

This directory contains the first implementation slice of the optional Windows compatibility layer for the current CakeOS repository. The historical HavenOS naming is retained in code paths and documentation where the platform migration has not yet renamed those interfaces.

## Implemented

- provider-based manifest model (`wine` and reserved `winboat` provider names)
- per-application Wine state/prefix locations
- fail-closed Bubblewrap requirement
- private network namespace only; network-enabled manifests are rejected in slice 1
- explicit host mount grants beneath `/mnt/haven-share/<name>` only
- broad and sensitive host filesystem grants rejected after path resolution
- explicit GPU render-node grant which fails if no render node exists
- Wayland-only display socket exposure for slice 1
- explicit refusal of clipboard/media permissions until enforceable mediation exists
- explicit refusal of the WinBoat provider until its VM/container boundary is implemented and proven
- one transient `systemd --user` service per launched Windows app
- deterministic hashed unit names rather than embedding app identifiers in unit names
- `KillMode=control-group` lifecycle ownership for full Wine process-tree shutdown
- clean service environment using `env -i`; unrelated user/session environment variables are not forwarded to Wine
- lifecycle status, stop, journal logs, reset, capability and health reporting
- reset/delete boundary restricted to the broker state root and refused while an app is running
- read-only host prerequisite audit for Wine and future WinBoat requirements

## Not implemented or claimed

- Wine installation or downloading
- a bundled Wine runtime
- WinBoat, Podman, Docker, QEMU/KVM, Windows installation, or FreeRDP orchestration
- X11 fallback
- PipeWire output-only mediation
- clipboard brokering
- Internet/LAN mediation
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

The manifest parser reserves `internet` and `lan` network values for the future HUI contract, but slice 1 deliberately rejects both. No network access is advertised or granted until a backend can enforce the distinction without simply sharing the host network namespace.

User-selected file/folder grants must target a child beneath `/mnt/haven-share/`. Host paths are resolved before launch so symlinks cannot be used to bypass the sensitive-path checks.

## Runtime layout

The broker expects versioned Wine runtimes beneath:

`$XDG_DATA_HOME/haven/compat/wine/runtimes/<runtime>/bin/wine`

and stores application state beneath:

`$XDG_DATA_HOME/haven/compat/wine/apps/<app-id>/`

The broker never installs a missing runtime and never falls back to an unsandboxed system Wine executable.

## Lifecycle

A launch is wrapped in a transient per-user systemd service named from a SHA-256 digest of the application ID. HUI therefore gets an authoritative lifecycle object instead of an unmanaged child PID.

The transient service uses:

- `KillMode=control-group`
- `TimeoutStopSec=10s`
- collection after the transient unit becomes inactive
- a clean `env -i` command environment containing only the broker-approved `PATH` and `LANG`
- Bubblewrap as the compatibility process executed by the service

The service manager itself receives only the minimum session routing environment required to reach the user's systemd manager. Windows application environment inheritance is intentionally separate from that control-plane environment.

`reset` first checks the unit state and refuses to delete an application's prefix while that application is still running.

## CLI / HUI boundary

From the repository root:

```sh
python3 -m compatibility.wine.haven_compat.cli audit
python3 -m compatibility.wine.haven_compat.cli capabilities
python3 -m compatibility.wine.haven_compat.cli health
python3 -m compatibility.wine.haven_compat.cli plan path/to/manifest.json
python3 -m compatibility.wine.haven_compat.cli launch path/to/manifest.json
python3 -m compatibility.wine.haven_compat.cli status path/to/manifest.json
python3 -m compatibility.wine.haven_compat.cli stop path/to/manifest.json
python3 -m compatibility.wine.haven_compat.cli logs path/to/manifest.json --lines 200
python3 -m compatibility.wine.haven_compat.cli reset path/to/manifest.json
```

The `capabilities` output is the intended stable HUI-facing feature contract for this slice. `health` combines host preflight facts with lifecycle-supervisor availability. `audit` and `health` are observational; they do not install, enable, reconfigure or create anything.

## Tests

The test suite is dependency-free:

```sh
python3 -m unittest tests.test_haven_compat -v
```

Tests validate manifest policy, sandbox-plan construction, lifecycle command construction, capability reporting and preflight evaluation. Passing tests are not runtime proof that Wine applications work on the approved HavenOS/CakeOS VM.

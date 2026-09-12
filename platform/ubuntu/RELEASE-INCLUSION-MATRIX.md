# HavenOS release inclusion matrix

This matrix records recovered evidence and keeps current verification separate
from historical handoffs. `UNKNOWN` means the evidence source has not been
recovered; it does not mean the component is absent.

| Component | Artifact/package | Version/revision | Dependencies | Launcher | Licence | Validation | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Ubuntu base | `C:\Temp\havenos-transfer\HavenOS-26.04.1-LTS-hui-preview-uefi-fixed-amd64.iso` | Ubuntu 26.04.1 LTS / `resolute`; 2,384,451,584 bytes; SHA-256 `BDF689CFC40C79F070000DAF0A069E9B31CA9FA1A0E7C4CBB7FD195E1C2F1216` | `platform/ubuntu/manifests/apt-sources.list` | `image/build-live-iso.sh` | Not revalidated in this recovery | ISO bytes and hash recovered; builder provenance/boot evidence not recovered | PARTIAL |
| GNOME Shell | Source submodule `platform/gnome-shell` | Expected `1d66bb011e56b1759a65eb8d3e9f194c92523baa`; current coordinator checkout `733a0886f1ba839b377be520972b7489a63a2b3a` | Ubuntu package list; Mutter | Live-build session | Not revalidated in this recovery | Provenance genuinely fails in coordinator checkout; builder revision UNKNOWN | BLOCKED |
| Mutter | Source submodule `platform/mutter` | Expected `90d032ae67645cfa1f78159e45b626ce479ba358`; current coordinator checkout `733a0886f1ba839b377be520972b7489a63a2b3a` | Ubuntu package list; GNOME Shell | Live-build session | Not revalidated in this recovery | Provenance genuinely fails in coordinator checkout; builder revision UNKNOWN | BLOCKED |
| Write | No recovered source/package handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Historical worker handoff not present in recovered branches or transfer evidence | UNKNOWN |
| Present | No recovered source/package handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Historical worker handoff not present in recovered branches or transfer evidence | UNKNOWN |
| Data | No recovered source/package handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Historical worker handoff not present in recovered branches or transfer evidence | UNKNOWN |
| Canvas/Rnote | No recovered source/package handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Historical worker handoff not present in recovered branches or transfer evidence | UNKNOWN |
| Boards/AppFlowy | No recovered source/package handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Historical worker handoff not present in recovered branches or transfer evidence | UNKNOWN |
| Wine compatibility | No recovered Wine runtime/EXE smoke handoff | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Wine remains the relevant compatibility scope; no current runtime proof recovered | UNKNOWN |
| Haven HUI runtime | `C:\Temp\havenos-transfer\haven-hui-preview_0.1.0+git2a578502c319_amd64.deb` | `0.1.0+git2a578502c319`; 35,328,774 bytes; SHA-256 `104F8FF3929B8B0E1C90673804D7CFBE79A31EA5C2763DC040246A7EAD53575A` | Debian package dependencies UNKNOWN | Package launcher/runtime UNKNOWN | Package metadata/licence not revalidated on Windows | Gate4 probe/install records: install passed, runtime self-test passed, platform invariant passed | PARTIAL |
| Local inference runtime / llama.cpp | Model-free contract in `HUI/llamacpp-provider.json`; no recovered `.deb` | Upstream revision `5266f24da75dc449bd56cbed7addb9c8e4a6a73e` | Linux builder required for package/install/binary smoke | Provider contract defined; image launcher UNKNOWN | Upstream MIT notice in runtime tree | HUI contract tests 3/3 pass; Linux package/install/smoke UNKNOWN | PARTIAL |

## Composition and validation status

- `platform/ubuntu/image/live-build/auto/config` targets Ubuntu `resolute`,
  `amd64`, and a live installer.
- The recovered ISO candidates and package are historical transfer evidence,
  not proof that the current Linux builder checkout produced the final image.
- `image/build-live-iso.sh` deliberately refuses to compose without a real
  `.deb` under `artifacts/packages`; the coordinator checkout has none.
- The approved VM is running at snapshot `f25b7456-c562-4ba4-9798-5814ee17fe25`.
  SSH authentication to the guest was not recovered in this session, so the
  builder revision and live `lb`/`dpkg-deb` output remain UNKNOWN.
- Android compatibility is intentionally not included in this matrix.

## Smallest supported integration change

No component is promoted to `READY` until current builder provenance, package
identity, image inclusion, and runtime/boot evidence are recovered. The
smallest correction for GNOME/Mutter is to reconcile the builder checkout with
the locked revisions without resetting or discarding dirty state.

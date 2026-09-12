# HavenOS release inclusion matrix

This matrix records only evidence available in the workspace. A component is
`NO` until its exact artifact or package, revision, launcher, licence, and
validation evidence are present.

| Component | Artifact/package | Version/revision | Dependencies | Launcher | Licence | Validation | Ready |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Ubuntu base | Ubuntu desktop ISO provenance in `havenos.lock` | Ubuntu 26.04.1 LTS / `resolute`; SHA-256 `601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f` | `platform/ubuntu/manifests/apt-sources.list` | `image/build-live-iso.sh` | Ubuntu terms not vendored or validated here | Hash is recorded; base ISO is not present locally | NO |
| GNOME Shell | Source submodule `platform/gnome-shell` | Locked revision `1d66bb011e56b1759a65eb8d3e9f194c92523baa` | Ubuntu package list; Mutter | `image/build-live-iso.sh` via live-build | Not evidenced in this workspace | `tests/verify-workspace.ps1` cannot validate because the submodule is uninitialized | NO |
| Mutter | Source submodule `platform/mutter` | Locked revision `90d032ae67645cfa1f78159e45b626ce479ba358` | Ubuntu package list; GNOME Shell | `image/build-live-iso.sh` via live-build | Not evidenced in this workspace | `tests/verify-workspace.ps1` cannot validate because the submodule is uninitialized | NO |
| Haven native UI/apps | `HUI/` and `apps/` source trees | No exact package or revision recorded in `havenos.lock` | Runtime/package artifacts not identified | No release launcher | Not evidenced in this workspace | Workspace layout only; no installable artifact or runtime validation | NO |
| Local inference runtime | `runtime/llamacpp` source and prior package commits | Upstream revision `5266f24da75dc449bd56cbed7addb9c8e4a6a73e`; no release `.deb` in this checkout | Haven package integration not identified | No image launcher | Not evidenced in this workspace | No package or image-level validation artifact present | NO |

## Composition and validation status

- `platform/ubuntu/image/live-build/auto/config` targets Ubuntu `resolute`,
  `amd64`, and a live installer.
- `platform/ubuntu/image/live-build/config/package-lists/havenos.list.chroot`
  contains only Ubuntu session packages; it does not prove a Haven package is
  included.
- `image/build-live-iso.sh` deliberately refuses to compose without at least
  one real `.deb` under `artifacts/packages`.
- The current checkout contains only `artifacts/.gitkeep`; no Haven `.deb` or
  ISO exists.
- The build host is missing `lb` and `jq`. VirtualBox is installed, but the
  configured VM/snapshot was not launched because image inputs are unverified.

## Smallest supported integration change

The only safe source change in this audit is removal of a stray patch-marker
line from `platform/ubuntu/packages/havenos-desktop.list`. No component is
promoted to `Ready` or copied into the image until package, revision, licence,
and validation evidence is supplied.

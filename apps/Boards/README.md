# Haven Boards AppFlowy foundation

This directory contains the first implementation slice for migrating Haven Boards into CakeOS/HUI while reusing AppFlowy Board only at a narrow, auditable boundary.

## Architecture

- `contract/` owns the Haven board data and command contracts. HUI talks to these contracts, not to Flutter types.
- `hui/` contains the HavenUI product projection. It renders neutral snapshots and emits typed commands, with no AppFlowy/Flutter/Avalonia dependency.
- `appflowy_poc/` is a standalone Flutter proof harness using `AppFlowy-IO/appflowy-board` at an exact upstream commit. It proves the upstream controller/reorder behaviour and serialises the same neutral snapshot shape used by the HUI-facing contract.
- `tests/` contains zero-install provenance/boundary gates and the runtime acceptance checklist.

Product rendering remains HUI-owned. The Flutter harness is not the CakeOS shell or final renderer. The AppFlowy dependency is intentionally isolated so that CakeOS can replace or upgrade it without changing persisted board documents or HUI scenes.

## Pinned upstream

- Repository: `https://github.com/AppFlowy-IO/appflowy-board`
- Commit: `804d7898ac0becabf73e45527baf5d5c573cd6bb`
- Selected licence: Mozilla Public License 2.0, using the upstream project's dual-licence grant (MPL-2.0 or AGPL-3.0).

No AppFlowy source has been copied into Haven-owned source files in this slice. The proof harness references the upstream package by Git commit.

See `THIRD_PARTY.md` before changing the pin or copying any upstream source.

## First-slice scope

Implemented source:

- three-column structured board proof;
- cards represented by stable IDs and titles;
- AppFlowy drag/reorder within a group;
- AppFlowy drag/move between groups;
- AppFlowy group reorder;
- local JSON persistence after AppFlowy controller mutations;
- explicit non-drag movement controls for keyboard/accessibility equivalence;
- neutral snapshot/command model independent of AppFlowy classes;
- HUI board scene projecting the neutral model;
- HUI command emission for group/card movement and card creation;
- hierarchy/attachment metadata preserved in the neutral card contract;
- zero-install static provenance and architecture boundary gate.

Not yet claimed:

- shared HUI runtime migrated/built in CakeOS;
- HUI runtime rendering in the approved Ubuntu VM;
- attachment file-store migration;
- freeform board rendering;
- collaboration/sync;
- AppFlowy package build success on the approved VM;
- accessibility runtime proof.

## Verification

Run from PowerShell once the CakeOS checkout is registered on the approved desktop:

```powershell
pwsh -NoProfile -File apps/Boards/tests/verify-boards-foundation.ps1
```

Then build/test the Flutter proof and the shared HUI runtime before changing the evidence state.

## Evidence states

Repository state alone is **implemented source**. Do not describe this slice as built, tested, packaged, booted, or runtime-proven until those stages are directly executed on the approved desktop/Ubuntu VM.

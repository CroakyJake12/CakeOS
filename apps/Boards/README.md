# Haven Boards AppFlowy foundation

This directory contains the first implementation slice for migrating Haven Boards into CakeOS/HUI while reusing AppFlowy Board only at a narrow, auditable boundary.

## Architecture

- `contract/` owns the Haven board data and command contracts. HUI talks to these contracts, not to Flutter types.
- `appflowy_poc/` is a standalone Flutter proof harness using `AppFlowy-IO/appflowy-board` at an exact upstream commit. It proves the upstream controller/reorder behaviour and serialises the same neutral snapshot shape used by the HUI-facing contract.
- Product rendering remains HUI-owned. The Flutter harness is not the CakeOS shell or final renderer.

The AppFlowy dependency is intentionally isolated so that CakeOS can replace or upgrade it without changing persisted board documents or HUI scenes.

## Pinned upstream

- Repository: `https://github.com/AppFlowy-IO/appflowy-board`
- Commit: `804d7898ac0becabf73e45527baf5d5c573cd6bb`
- Selected licence: Mozilla Public License 2.0, using the upstream project's dual-licence grant (MPL-2.0 or AGPL-3.0).

No AppFlowy source has been copied into Haven-owned source files in this slice. The proof harness references the upstream package by Git commit.

See `THIRD_PARTY.md` before changing the pin or copying any upstream source.

## First-slice scope

Implemented in the proof harness:

- three-column structured board;
- cards represented by stable IDs and titles;
- drag/reorder within a group;
- drag/move between groups;
- group reorder;
- local JSON persistence after AppFlowy controller mutations;
- explicit non-drag movement buttons for keyboard/accessibility equivalence;
- neutral snapshot export independent of AppFlowy classes.

Not yet claimed:

- HUI runtime rendering in the approved Ubuntu VM;
- attachment migration;
- freeform board rendering;
- collaboration/sync;
- AppFlowy package build success on the approved VM;
- accessibility/runtime proof.

## Evidence states

Repository state alone is **implemented source**, not build/runtime evidence. A later worker must run the proof harness and HUI adapter on the approved desktop/Ubuntu VM before marking this slice built, tested, packaged, booted, or runtime-proven.

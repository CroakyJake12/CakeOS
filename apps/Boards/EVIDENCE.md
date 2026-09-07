# Haven Boards evidence ledger

This file records what has actually been executed for the `boards-appflowy-foundation` migration. It intentionally distinguishes source inspection, implementation, build/test evidence, and runtime proof.

## Upstream and donor

- AppFlowy Board repository/source/API: **INSPECTED**.
- AppFlowy Board licence boundary: **INSPECTED**; this slice selects the upstream MPL-2.0 option.
- AppFlowy Board revision: **PINNED** to `804d7898ac0becabf73e45527baf5d5c573cd6bb`.
- The Flutter proof lockfile resolves `appflowy_board` to that same exact revision: **PINNED / RESOLVED**.
- CakeAI Boards donor implementation: **INSPECTED**.
- CakeAI HUI architecture/API used by the Boards projection: **INSPECTED**.
- Real HUI donor compatibility revision: **PINNED** to CakeAI commit `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb` in a separate sparse, detached checkout used only for compatibility testing.
- AppFlowy source copied into CakeOS-owned source files: **NO**.
- External AppFlowy fork created: **NO**.

## CakeOS implementation

- Neutral board snapshot/command/reducer contract: **IMPLEMENTED, BUILT, TESTED**.
- AppFlowy callback-to-neutral-command adapter: **IMPLEMENTED, TESTED**.
- JSON local-first store with durable temp write and backup recovery: **IMPLEMENTED, BUILT, TESTED**.
- HUI structured-board projection: **IMPLEMENTED, BUILT, TESTED AGAINST REAL PINNED HUI DONOR API**.
- HUI disabled controls synchronize visual property, accessibility state, and `HavenElementState.Disabled`: **IMPLEMENTED, TESTED**.
- Composed HUI application session (`HavenBoardsHuiSession`) binding scene commands to durable store writes: **IMPLEMENTED, BUILT, TESTED**.
- Flutter/AppFlowy bounded proof harness: **IMPLEMENTED, ANALYZED, TESTED**.
- Explicit non-drag group/card movement controls: **IMPLEMENTED, TESTED IN FLUTTER; HUI KEYBOARD COMMAND PATH TESTED**.
- Hierarchy metadata in the neutral card schema: **IMPLEMENTED IN SCHEMA / PERSISTENCE**; hierarchy mutation invariants are the next gap.
- Content-addressed local attachment blob store plus typed attachment metadata commands: **IMPLEMENTED, BUILT, TESTED**.
- Attachment deduplication, bounded imports, display-name path isolation, malformed-reference rejection, reparse/link rejection, and existing-blob digest verification: **IMPLEMENTED, TESTED**.
- Attachment blob + board metadata + HUI attachment-count + reopen/readback composition: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Content-blob deletion/reference counting: **DEFERRED** until a safe ownership/reference index exists.
- Freeform HUI board renderer: **NOT IMPLEMENTED**.
- Collaboration/sync: **DEFERRED / NOT IMPLEMENTED**.

## Executed on approved desktop

Approved host: `DESKTOP-7CHJ9S6`.

A clean CakeOS checkout was created separately from the dirty legacy Haven AI checkout at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeOS`

The approved/current branch is `boards-appflowy-foundation`.

An isolated sparse CakeAI donor checkout used only for real-HUI compatibility testing was created at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeAI-HUI-Proof`

It was detached at commit `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb` and sparse-limited to `src/Haven.UI`. The dirty legacy Haven AI checkout was not used or modified.

Executed positive evidence:

1. Strengthened `apps/Boards/tests/verify-boards-foundation.ps1` through Windows PowerShell on the current tested head: **PASSED, exit 0**.
2. `dotnet build apps/Boards/contract/CakeOS.Apps.Boards.Contract.csproj --configuration Debug`: **PASSED, exit 0**.
3. Neutral reducer/store regression `dotnet test apps/Boards/tests/CakeOS.Apps.Boards.Tests.csproj --configuration Debug`: **PASSED, exit 0** after adding the attachment reducer/store tests.
4. A portable Flutter SDK was cloned under the Development root; no system-wide Flutter installation was made.
5. `flutter pub get` in `apps/Boards/appflowy_poc`: **PASSED, exit 0**. The exact pinned AppFlowy Board dependency resolved successfully.
6. Latest `flutter analyze` after the deterministic persistence/test refactor: **PASSED, exit 0, no diagnostics**.
7. Current-head isolated AppFlowy controller reorder test: **PASSED, exit 0**.
8. Deterministic AppFlowy widget/accessibility test with filesystem persistence explicitly disabled: **PASSED, exit 0**.
9. Complete corrected `flutter test` suite: **PASSED, exit 0**.
10. `apps/Boards/tests/verify-hui-compatibility.ps1` against the real pinned donor `src/Haven.UI/Haven.UI.csproj`: **PASSED, exit 0**. This compiled the CakeOS Boards HUI project against the actual donor HUI project and ran the HUI scene tests.
11. Composed local-first HUI lifecycle tests: **PASSED, exit 0**. They cover direct command/save/dispose/reopen and a keyboard-originated HUI move command followed by queue flush, disposal, fresh store/session reopen, and verification of the moved card state.
12. Content-addressed attachment contract/store tests: **PASSED, exit 0**. They cover deduplication, display-name path escape attempts, oversize cleanup, unsafe IDs/malformed references, byte readback, and tampered existing-blob rejection.
13. Real-HUI compatibility was rerun after the attachment integration test was added: **PASSED, exit 0**. The composed test imports real bytes, attaches returned metadata to a card, observes `1 attachment` in HUI, disposes/reopens the board, observes the same HUI metadata again, and reopens identical bytes from the blob store.
14. The strengthened static gate checks the composed session’s persist-before-publish ordering, absence of network primitives, content-addressing, bounded attachment imports, display-name isolation, link/reparse protection, and the matching executable tests: **PASSED, exit 0**.
15. Repository hygiene inspection after Flutter testing identified only generated Flutter state. `.dart_tool` is explicitly ignored and `pubspec.lock` is committed for proof-harness reproducibility; the resulting local dependency-pin commit reported **no remaining changes** before push.

## Negative evidence retained

- The first Flutter widget-test revision used asynchronous filesystem restore/write from `initState` and did not terminate normally.
- Replacing `pumpAndSettle()` with bounded pumps alone did not fix that revision.
- The isolated bounded widget test job `c9bf4272-dcf7-4a70-a1ff-f33f00667e94` was terminated by the coordinator after the approved 300-second limit with `COMMANDTIMEOUT` and exit `-1`.
- The harness was then separated into normal persistence-on execution and deterministic persistence-off UI testing. The corrected deterministic widget test and complete corrected Flutter suite subsequently passed.
- Therefore the timeout is preserved as a harness-lifecycle failure that was fixed; it is not described as upstream AppFlowy runtime failure.

## Not yet proven

The following must not be described as proven yet:

- HUI scene/session compilation against a CakeOS-owned shared HUI runtime after that runtime is permanently landed in CakeOS; current proof uses the exact real CakeAI donor HUI project through a fail-closed compatibility reference;
- approved Ubuntu VM execution;
- Linux package installation/runtime;
- packaged-process offline terminate/reopen on CakeOS/Ubuntu. The composed desktop component lifecycle is tested and contains no network dependency, but that is not the same as final packaged offline runtime proof;
- pointer drag interoperability in final HUI rendering;
- assistive-technology runtime accessibility with a screen reader or other AT;
- content-blob deletion/reference counting and GC policy;
- hierarchy mutation/cycle-prevention behavior beyond persisted `ParentCardId` metadata;
- freeform board runtime;
- realtime collaboration/sync.

## Acceptance rule

Only promote evidence states after directly running the matching stage. Source presence is not build evidence; build success is not runtime proof; desktop component proof is not packaged-process or approved-VM/Linux proof.

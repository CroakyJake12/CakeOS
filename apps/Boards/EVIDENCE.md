# Haven Boards evidence ledger

This file records what has actually been executed for the `boards-appflowy-foundation` migration. It intentionally distinguishes source inspection, implementation, build/test evidence, and runtime proof.

## Upstream and donor

- AppFlowy Board repository/source/API: **INSPECTED**.
- AppFlowy Board licence boundary: **INSPECTED**; this slice selects the upstream MPL-2.0 option.
- AppFlowy Board revision: **PINNED** to `804d7898ac0becabf73e45527baf5d5c573cd6bb`.
- CakeAI Boards donor implementation: **INSPECTED**.
- CakeAI HUI architecture/API used by the Boards projection: **INSPECTED**.
- AppFlowy source copied into CakeOS-owned source files: **NO**.
- External AppFlowy fork created: **NO**.

## CakeOS implementation

- Neutral board snapshot/command/reducer contract: **IMPLEMENTED**.
- AppFlowy callback-to-neutral-command adapter: **IMPLEMENTED**.
- JSON local-first store with durable temp write and backup recovery: **IMPLEMENTED**.
- HUI structured-board projection: **IMPLEMENTED SOURCE**, not yet built because the shared CakeOS HUI runtime has not landed in this branch.
- Flutter/AppFlowy bounded proof harness: **IMPLEMENTED**.
- Explicit non-drag group/card movement controls: **IMPLEMENTED**.
- Hierarchy and attachment metadata in neutral card schema: **IMPLEMENTED**.
- Freeform HUI board renderer: **NOT IMPLEMENTED**.
- Collaboration/sync: **DEFERRED / NOT IMPLEMENTED**.

## Executed on approved desktop

Approved host: `DESKTOP-7CHJ9S6`.

A clean CakeOS checkout was created separately from the dirty legacy Haven AI checkout at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeOS`

The approved/current branch was `boards-appflowy-foundation`.

Executed evidence:

1. `apps/Boards/tests/verify-boards-foundation.ps1` through Windows PowerShell: **PASSED, exit 0**.
2. `dotnet build apps/Boards/contract/CakeOS.Apps.Boards.Contract.csproj --configuration Debug`: **PASSED, exit 0**.
3. `dotnet test apps/Boards/tests/CakeOS.Apps.Boards.Tests.csproj --configuration Debug`: **PASSED, exit 0**.
4. A portable Flutter SDK was cloned under the Development root; no system-wide Flutter installation was made.
5. `flutter pub get` in `apps/Boards/appflowy_poc`: **PASSED, exit 0**. The pinned AppFlowy Board dependency resolved successfully.
6. `flutter analyze` for the proof harness, before the later widget-test file was added: **PASSED, exit 0, no diagnostics**.
7. A subsequent full `flutter test` run against the first widget-test revision remained long-running without diagnostics. The likely indefinite `pumpAndSettle()` waits were removed in commit `df9012d2bdcc0b062d96437aaed47e2d48bb8eaa`. **The corrected Flutter tests are not yet claimed passed.**

## Not yet proven

The following must not be described as proven yet:

- corrected Flutter controller/widget tests passing;
- HUI scene compilation against a CakeOS-owned shared HUI runtime;
- approved Ubuntu VM execution;
- Linux package installation/runtime;
- complete offline terminate/reopen acceptance using the final CakeOS app;
- pointer drag interoperability in final HUI rendering;
- assistive-technology runtime accessibility;
- attachment file-store migration;
- freeform board runtime;
- realtime collaboration/sync.

## Acceptance rule

Only promote evidence states after directly running the matching stage. Source presence is not build evidence; build success is not runtime proof; desktop proof is not approved-VM/Linux proof.

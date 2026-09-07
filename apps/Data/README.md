# Haven Data — Calc + DuckDB first slice

Status: **implemented and runtime-proven on disposable Ubuntu 24.04 and Ubuntu 26.04.1 CI; not yet accepted inside the approved CakeOS VM**.

This directory is the first CakeOS-native Haven Data slice. It intentionally does not copy the legacy donor implementation or LibreOffice/DuckDB upstream source. HUI-facing code depends only on Haven-owned interfaces; LibreOffice Calc and DuckDB live behind separate worker processes.

## Boundary

```text
HUI / generated UI
        |
DataGridSession / DataQuerySession / DataAppService
   |                         |
IDataSpreadsheetEngine       IDataDatabaseEngine
   |                         |
CalcSpreadsheetEngine        DuckDbDatabaseEngine
   |                         |
JSON-lines stdio             JSON-lines stdio
   |                         |
calc_worker.py                duckdb_worker.py
   |                         |
UNO local pipe                DuckDB connection
   |                         |
headless LibreOffice Calc     local .duckdb file
          |                   ^
          +-- DataWorkbookDatabaseBridge --+
               typed snapshot publication
```

No UNO type or DuckDB API is exposed to HUI. The workbook remains Calc-owned; DuckDB receives explicit snapshot tables rather than shared document authority. Raw query text never receives the structured schema-mutation capability used by the snapshot bridge.

## Evidence ledger

| Claim | Evidence |
| --- | --- |
| Source implemented | Yes — CakeOS feature branch `data-calc-duckdb-first-slice` |
| .NET 10 build | Yes — Ubuntu 24.04 and Ubuntu 26.04.1 GitHub Actions, warnings treated as errors |
| Grid/query/bridge smoke | Yes — 10 × 8 grid session, sheet selection, edit/recalculate/refresh, save delegation, typed workbook→database publication, bounded query execution and 20-entry recent-query history |
| Worker Python syntax | Yes — both workers and all Python integration/corpus harnesses compile with `py_compile` |
| DuckDB runtime | Yes — pinned DuckDB 1.5.5 opens disposable databases, executes bounded SELECTs, rejects DDL/multiple statements/external file access, accepts typed table publication, safely quotes identifiers and atomically replaces snapshots |
| Calc/UNO runtime | Yes — live headless Calc opens disposable ODS files over a local UNO pipe, edits cells/formulas, recalculates, saves/reopens ODS and XLSX, and enforces read-only/save-as guards |
| Formula compatibility baseline | Yes — live Calc corpus currently covers arithmetic precedence, SUM, absolute references, exponentiation, AVERAGE, COUNT, MIN and MAX |
| Full .NET→worker runtime path | Yes — real `CalcSpreadsheetEngine` + `DuckDbDatabaseEngine` edit/recalculate a workbook, publish it into DuckDB, aggregate it with SQL, save it and reopen the saved ODS |
| Ubuntu 26.04.1 distro proof | Yes — GitHub Actions `ubuntu-26.04` runner, LibreOffice `26.2.5.2-0ubuntu0.26.04.1`, Python 3.14 and DuckDB 1.5.5; full runtime gate passed |
| Worker protocol cancellation safety | Implemented — once an in-flight request may have been written, cancellation or malformed/misaligned response faults and terminates the worker rather than reusing a desynchronised protocol stream |
| Approved desktop checkout | Located — Sandbox project `cakeos` at `C:\Users\Jacob\OneDrive\Personal Files\Development\CakeOS`; guarded switch to this branch is currently refused because the existing Boards checkout has tracked/untracked changes that must be preserved |
| Approved CakeOS VM | **No** — the actual CakeOS VM/session has not run this branch |
| CakeOS package/image | **No** — no package list, image, ISO or VM state was changed by this branch |
| Visual HUI verification | **No** — `HUI/` currently has no usable renderer contract on this branch, so no fake UI integration is claimed |

The Ubuntu 26.04.1 CI proof materially narrows distro/runtime risk, but it is deliberately **not** labelled as approved-VM, packaged-image or visual-runtime acceptance.

## Implemented spreadsheet behaviour

- starts a dedicated headless `soffice` process;
- creates a disposable LibreOffice user profile for the worker;
- connects through a local UNO pipe rather than a TCP listener;
- disables macro execution and document-link updates at load;
- rejects non-Calc documents;
- enumerates real worksheet names;
- opens a workbook hidden and optionally read-only;
- reads bounded ranges;
- edits text, numbers and formulas;
- requests full recalculation;
- permits first-slice save-as to `.ods` or `.xlsx`, not in-place overwrite;
- verifies that a save produced a non-empty output;
- closes documents and tears down the worker/profile;
- drains child-process diagnostics and uses bounded shutdown/kill fallback.

## Implemented HUI-facing spreadsheet session

`DataGridSession` exposes only Haven-owned records and operations:

- fixed 10 × 8 first-slice viewport;
- real Calc worksheet discovery and selection;
- returned-grid dimension validation;
- viewport-bounded edits;
- edit → recalculate → refresh as one typed app operation;
- save-as and deterministic close/dispose semantics;
- atomic workbook-open failure cleanup.

The real HUI renderer remains a separate platform seam. This app layer is ready to supply it with typed state without leaking UNO/VCL objects.

## Implemented database/query behaviour

- opens a local DuckDB database;
- pins DuckDB 1.5.5 in CI/runtime proof;
- disables external access, extension auto-install/auto-load, community extensions and persistent secrets;
- applies bounded worker memory/thread defaults and locks DuckDB configuration after setup;
- raw user/model query text permits only a single `SELECT` or `EXPLAIN` statement;
- mutation/capability keywords are rejected again inside the worker;
- query previews are capped at 1–1000 rows;
- `DataQuerySession` tracks typed published tables and the 20 most recent successful queries;
- query-session close is atomic: logical state is retained if engine close fails so the caller can retry;
- no database API is exposed directly to generated UI.

## Workbook→database bridge

- publishes an explicit displayed-value range snapshot rather than sharing workbook ownership;
- supports generated A/B/C… column names or sanitised first-row headers;
- validates source sheet, coordinates and dimensions before publication;
- uses privileged structured `ReplaceTableAsync` rather than exposing DDL strings;
- validates table/column names and shape at app and worker boundaries;
- quotes SQL identifiers and parameterises row values;
- replaces the target table inside a DuckDB transaction;
- keeps raw user/model SQL read-only after publication capability is enabled.

Current publication is intentionally string/display-value based. Typed cell/date/error preservation is a later compatibility milestone.

## Worker protocol safety

`JsonLineWorkerClient` serialises calls per worker. If cancellation happens after a request may have been written, or if stdout contains malformed JSON, a missing result, EOF or a mismatched response ID, the client faults and terminates that worker process. It does not attempt to continue on a potentially shifted request/response stream.

This prevents a cancelled request's late response from being consumed as the next operation's response.

## Runtime dependencies — not installed into CakeOS by this branch

The Calc worker expects:

- LibreOffice Calc headless runtime (`soffice`);
- Python with LibreOffice `pyuno`/`uno` bindings;
- Calc filters needed for ODS/XLSX.

The DuckDB worker expects Python and the approved/pinned DuckDB runtime package. The worker is deliberately process-local so a later move from the Python binding to the DuckDB C API does not change the HUI-facing contracts.

The Ubuntu 26.04.1 CI lane proved the narrow distro packages `libreoffice-calc-nogui`, `libreoffice-core-nogui`, `libreoffice-common` and `python3-uno` are sufficient for the current integration tests. This branch itself does **not** install those packages into the CakeOS image or approved VM.

## Reproducible checks

```bash
dotnet build apps/Data/App/HavenOS.Data.App.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release
python3 -m py_compile apps/Data/workers/calc_worker.py
python3 -m py_compile apps/Data/workers/duckdb_worker.py
python3 -m py_compile apps/Data/Tests/test_workers.py
python3 -m py_compile apps/Data/Tests/test_duckdb_publish.py
python3 -m py_compile apps/Data/Tests/test_calc_formula_corpus.py
```

After disposable LibreOffice/UNO and DuckDB runtimes are available:

```bash
python3 apps/Data/Tests/test_workers.py
python3 apps/Data/Tests/test_duckdb_publish.py
python3 apps/Data/Tests/test_calc_formula_corpus.py
dotnet run --project apps/Data/Tests/HavenOS.Data.Runtime.csproj -c Release
```

`.github/workflows/data-first-slice.yml` runs these gates on both Ubuntu 24.04 and Ubuntu 26.04.

## Approved-VM gate still required

Use disposable fixture/output/database paths. Do not use normal user documents.

1. Preserve the existing dirty Boards checkout; do not clean, stash, reset or overwrite it for Data testing.
2. When that work has been safely preserved by its owner, switch the registered CakeOS checkout to `data-calc-duckdb-first-slice` only through Sandbox's guarded branch-switch operation.
3. Confirm the approved VM identity, Ubuntu 26.04.1 environment and current package state before mutation.
4. Build the Data projects without package/image mutation first.
5. If runtime dependencies are absent, record that fact before any installation; add them only through the approved platform/package path.
6. Run the same disposable Python and .NET integration gates.
7. Verify worker cleanup, local-pipe-only UNO communication, filesystem/network confinement, resource limits and restart/failure behaviour in the real session.

## HUI gate still required

Once the platform provides a real HUI renderer contract:

1. Render the `DataGridSession` 10 × 8 snapshot through HUI.
2. Select a real worksheet discovered by the engine.
3. Edit text/numeric cells and a dependent formula through typed commands.
4. Verify focus, selection, keyboard navigation and screen-reader semantics.
5. Render query results from `DataQuerySession` without exposing raw engine objects.
6. Verify all generated-UI actions map to typed Haven operations rather than unrestricted UNO/SQL authority.

## Deliberately deferred

- direct linking to LibreOffice `sc/` internals;
- embedding LibreOffice/VCL UI;
- LibreOfficeKit rendering;
- charts and drawings;
- formatting/validation/named ranges;
- raw SQL mutation/DDL;
- DuckDB query-result materialisation back into a Calc range/sheet;
- typed preservation beyond displayed strings when publishing Calc ranges to DuckDB;
- package/image changes;
- unrestricted generated-UI write operations.

## Safety notes

The worker boundary is defence in depth, not the final OS sandbox. Before shipping, Calc and DuckDB still need target-runtime confinement (AppArmor/systemd/bubblewrap or the platform-selected equivalent), explicit filesystem brokers, resource limits, crash supervision, a larger golden-file/formula corpus and visual/accessibility acceptance through HUI.

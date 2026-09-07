# Haven Data — Calc + DuckDB first slice

Status: **implemented and runtime-proven on disposable Ubuntu 24.04 CI; not yet accepted on the approved CakeOS Ubuntu 26.04.1 VM**.

This directory is the first CakeOS-native Haven Data slice. It intentionally does not copy the legacy donor implementation or LibreOffice/DuckDB upstream source. HUI-facing code depends only on Haven-owned interfaces; LibreOffice Calc and DuckDB live behind separate worker processes.

## Boundary

```text
HUI / generated UI
        |
DataGridSession / DataAppService
   |          |
IDataSpreadsheetEngine   IDataDatabaseEngine
   |          |
CalcSpreadsheetEngine    DuckDbDatabaseEngine
   |          |
JSON-lines stdio         JSON-lines stdio
   |          |
calc_worker.py            duckdb_worker.py
   |          |
UNO local pipe            DuckDB connection
   |          |
headless LibreOffice Calc local .duckdb file
```

No UNO type or DuckDB API is exposed to HUI.

## Evidence ledger

| Claim | Evidence |
| --- | --- |
| Source implemented | Yes — CakeOS feature branch `data-calc-duckdb-first-slice` |
| .NET 10 build | Yes — Ubuntu 24.04 GitHub Actions, 0 warnings / 0 errors |
| Contract smoke | Yes — Ubuntu 24.04 GitHub Actions |
| Worker Python syntax | Yes — both workers plus integration harness compiled with `py_compile` |
| DuckDB runtime | Yes — DuckDB 1.5.5 opened a disposable database, executed bounded SELECTs, and rejected DDL/multiple statements/external file access |
| Calc/UNO runtime | Yes — LibreOffice 24.2.7 on Ubuntu 24.04 opened a disposable ODS over a local UNO pipe, edited cells/formula, recalculated, saved/reopened ODS and XLSX, and enforced read-only/save-as guards |
| Approved CakeOS VM | **No** — CakeOS is not currently registered in Sandbox, so Ubuntu 26.04.1 VM acceptance has not been executed |
| CakeOS package/image | **No** — no package list, image, ISO or VM state was changed |
| Visual HUI verification | **No** — the target HUI renderer is not yet wired into this app slice |

The CI runtime proof is useful engineering evidence but is deliberately not labelled as CakeOS VM acceptance.

## Implemented P0/P1 behaviour

Spreadsheet side:

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
- first slice permits save-as to `.ods` or `.xlsx`, not in-place overwrite;
- verifies that a save produced a non-empty output;
- closes documents and tears down the worker/profile;
- drains child-process diagnostics and has bounded shutdown/kill fallback.

Database side:

- opens a local DuckDB database;
- pins DuckDB 1.5.5 in CI/runtime proof;
- disables external access, extension auto-install/auto-load, community extensions and persistent secrets;
- applies bounded worker memory/thread defaults and locks DuckDB configuration after setup;
- first slice permits only a single `SELECT` or `EXPLAIN` statement;
- mutation/capability keywords are rejected again inside the worker;
- query previews are capped at 1–1000 rows;
- no database API is exposed directly to generated UI.

## Runtime dependencies — not installed by this branch

The Calc worker expects these capabilities to be provided by the target image or a disposable POC environment:

- LibreOffice Calc headless runtime (`soffice`);
- Python 3 with LibreOffice `pyuno`/`uno` bindings;
- the LibreOffice Calc filters needed for ODS/XLSX.

The DuckDB worker expects Python 3 and the approved/pinned DuckDB runtime package. The worker is deliberately kept process-local so a later switch from the Python binding to the DuckDB C API does not change the HUI contract.

This feature branch does **not** install packages into CakeOS or modify the approved VM/image.

## Reproducible source/runtime checks

```bash
dotnet build apps/Data/App/HavenOS.Data.App.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release
python3 -m py_compile apps/Data/workers/calc_worker.py
python3 -m py_compile apps/Data/workers/duckdb_worker.py
python3 -m py_compile apps/Data/Tests/test_workers.py
```

The GitHub workflow `.github/workflows/data-first-slice.yml` additionally provisions disposable LibreOffice/UNO and DuckDB runtimes and runs `apps/Data/Tests/test_workers.py` end to end.

## Approved-VM P0 gate still required

Use disposable fixture/output/database paths. Do not use normal user documents.

1. Register/resolve the existing CakeOS checkout in Sandbox without replacing the VM or dirty work.
2. Confirm the approved Ubuntu 26.04.1 environment and current package state.
3. Build the Data projects without image/package mutation first.
4. If runtime dependencies are absent, record that fact before any package installation; install only with the platform/package worker's approved path.
5. Run the same Calc/DuckDB disposable integration tests.
6. Verify worker cleanup, local-pipe-only UNO communication and filesystem/network confinement.

## P1 HUI gate still required

1. Render the `DataGridSession` 10 × 8 snapshot through HUI.
2. Select a real worksheet discovered by the engine.
3. Edit one text cell and one numeric cell.
4. Set one formula with a dependency on an edited cell.
5. Recalculate and verify the refreshed grid value.
6. Save to a new `.ods` fixture, close/reopen and verify.
7. Repeat save/reopen to `.xlsx` and record compatibility differences.
8. Visually verify keyboard focus, selection, screen-reader semantics and HUI-only rendering.

## Deliberately deferred

- direct linking to LibreOffice `sc/` internals;
- embedding LibreOffice/VCL UI;
- LibreOfficeKit rendering;
- charts and drawings;
- formatting/validation/named ranges;
- SQL mutation/DDL;
- sheet-to-DuckDB publication and query-result materialisation;
- package/image changes;
- unrestricted generated-UI write operations.

## Safety notes

The worker boundary is defence in depth, not the final OS sandbox. Before shipping, Calc and DuckDB still need target-runtime confinement (AppArmor/systemd/bubblewrap or the platform-selected equivalent), explicit filesystem brokers, resource limits, crash supervision and golden-file compatibility tests.

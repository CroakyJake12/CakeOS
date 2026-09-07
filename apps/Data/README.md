# Haven Data — Calc + DuckDB first slice

Status: **implemented in source, not yet runtime-proven**.

This directory is the first CakeOS-native Haven Data slice. It intentionally does not copy the legacy donor implementation or LibreOffice/DuckDB upstream source. HUI-facing code depends only on Haven-owned interfaces; LibreOffice Calc and DuckDB live behind separate worker processes.

## Boundary

```text
HUI / generated UI
        |
DataAppService
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

## Implemented P0/P1 behaviour

Spreadsheet side:

- starts a dedicated headless `soffice` process;
- creates a disposable LibreOffice user profile for the worker;
- connects through a local UNO pipe rather than a TCP listener;
- opens an existing workbook hidden and optionally read-only;
- reads bounded ranges;
- edits text, numbers and formulas;
- requests full recalculation;
- saves as `.ods` or `.xlsx`;
- closes documents and tears down the worker/profile.

Database side:

- opens a local DuckDB database;
- disables DuckDB external access and extension auto-install/auto-load;
- first slice permits only a single `SELECT` or `EXPLAIN` statement;
- mutation/capability keywords are rejected again inside the worker;
- query previews are capped at 1–1000 rows;
- no database API is exposed directly to generated UI.

## Runtime dependencies — not installed by this branch

The Calc worker expects these capabilities to already be provided by the target image or disposable POC environment:

- LibreOffice Calc headless runtime (`soffice`);
- Python 3 with LibreOffice `pyuno`/`uno` bindings;
- the LibreOffice Calc filters needed for ODS/XLSX.

The DuckDB worker expects Python 3 and the approved/pinned DuckDB runtime package. The worker is deliberately kept process-local so the eventual implementation can replace the Python binding with the DuckDB C API without changing the HUI contract.

This feature branch does **not** install any packages or modify the CakeOS image/VM.

## Build/smoke commands once CakeOS is registered in Sandbox

```bash
dotnet build apps/Data/App/HavenOS.Data.App.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release
python3 -m py_compile apps/Data/workers/calc_worker.py
python3 -m py_compile apps/Data/workers/duckdb_worker.py
```

These checks establish source/build validity only. They do not prove LibreOffice or DuckDB runtime integration.

## P0 runtime gate

Use a disposable fixture workbook and disposable output/database paths. Do not use a user's normal documents.

1. Confirm `soffice`, `python3`, `uno` and DuckDB are present in the approved VM/POC environment.
2. Start `calc_worker.py` through `CalcSpreadsheetEngine`.
3. Open the fixture workbook.
4. Read a small range and verify sheet values.
5. Close it and verify the temporary LibreOffice profile is removed.
6. Start the DuckDB worker, open a disposable database, and prove a `SELECT` succeeds while `CREATE`, `COPY`, `ATTACH` and multiple statements fail.

## P1 runtime gate

With a disposable workbook copy:

1. Render a HUI grid from `ReadRangeAsync` (target first slice: 10 rows × 8 columns).
2. Edit one text cell and one numeric cell.
3. Set one formula with a dependency on an edited cell.
4. Call `RecalculateAsync` and verify the calculated value through another range read.
5. Save to a new `.ods` fixture path.
6. Close/reopen the saved copy and verify all three cells.
7. Repeat save/reopen to `.xlsx` and record any compatibility differences.

Do not claim the slice as runtime-proven until those steps have been completed on the approved Ubuntu environment.

## Deliberately deferred

- direct linking to LibreOffice `sc/` internals;
- embedding LibreOffice/VCL UI;
- LibreOfficeKit rendering;
- charts and drawings;
- formatting/validation/named ranges;
- SQL mutation/DDL;
- sheet-to-DuckDB publication and query-result materialisation;
- package/image changes;
- generated-UI write operations beyond the typed Haven interfaces.

## Safety notes

The worker boundary is a defence-in-depth layer, not the final OS sandbox. Before shipping, the Calc and DuckDB workers still need target-runtime confinement (AppArmor/systemd/bubblewrap or the platform-selected equivalent), explicit filesystem brokers, resource limits, crash supervision, and golden-file compatibility tests.

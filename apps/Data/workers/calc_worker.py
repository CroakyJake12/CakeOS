#!/usr/bin/env python3
"""Haven Data Calc worker.

Repository-side P0/P1 worker. Requires LibreOffice/pyuno at runtime.
The worker owns an isolated LibreOffice profile and communicates only over stdin/stdout.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path

try:
    import uno
    from com.sun.star.beans import PropertyValue
except Exception as exc:  # pragma: no cover - runtime dependency gate
    print(json.dumps({"id": 0, "error": f"pyuno is unavailable: {exc}", "result": None}), flush=True)
    raise SystemExit(78)


MAX_MATERIALIZED_ROWS = 1000
MAX_MATERIALIZED_COLUMNS = 256
MAX_STRUCTURAL_MUTATION_COUNT = 100
MAX_NAMED_RANGE_NAME_LENGTH = 64
PORTABLE_SHEET_FORBIDDEN = set("[]:*?/\\")


def prop(name: str, value):
    item = PropertyValue()
    item.Name = name
    item.Value = value
    return item


def portable_sheet_name(value: object) -> str:
    text = str(value or "").strip()
    if not text:
        raise ValueError("sheetName is required.")
    if len(text) > 31:
        raise ValueError("Materialized sheet names must be at most 31 characters for ODS/XLSX portability.")
    if any(ord(character) < 32 or ord(character) == 127 or character in PORTABLE_SHEET_FORBIDDEN for character in text):
        raise ValueError("Materialized sheet name contains a character that is unsafe for ODS/XLSX portability.")
    if text.startswith("'") or text.endswith("'"):
        raise ValueError("Materialized sheet names cannot begin or end with an apostrophe.")
    return text


def named_range_name(value: object) -> str:
    text = str(value or "").strip()
    if not text:
        raise ValueError("Named range name is required.")
    if len(text) > MAX_NAMED_RANGE_NAME_LENGTH:
        raise ValueError(f"Named range names must be at most {MAX_NAMED_RANGE_NAME_LENGTH} characters in this slice.")
    if not (text[0].isalpha() or text[0] == "_"):
        raise ValueError("Named range names must start with a letter or underscore.")
    if any(not (character.isalnum() or character in "_.") for character in text[1:]):
        raise ValueError("Named range names may contain only letters, digits, underscores or periods after the first character.")
    return text


class CalcRuntime:
    def __init__(self) -> None:
        self.profile_dir = tempfile.mkdtemp(prefix="haven-data-lo-")
        self.stderr_path = Path(self.profile_dir) / "soffice.stderr.log"
        self.stderr_file = open(self.stderr_path, "w+", encoding="utf-8")
        self.pipe_name = f"haven_data_{uuid.uuid4().hex}"
        profile_url = uno.systemPathToFileUrl(self.profile_dir)
        accept = f"--accept=pipe,name={self.pipe_name};urp;StarOffice.ComponentContext"
        self.process = subprocess.Popen(
            [
                os.environ.get("HAVEN_DATA_SOFFICE", "soffice"),
                "--headless",
                "--nologo",
                "--nodefault",
                "--nofirststartwizard",
                "--norestore",
                f"-env:UserInstallation={profile_url}",
                accept,
            ],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=self.stderr_file,
            text=True,
        )
        self.context = None
        self.smgr = None
        self.desktop = None
        self.documents: dict[str, object] = {}
        self.paths: dict[str, str] = {}
        try:
            self.context = self._connect()
            self.smgr = self.context.ServiceManager
            self.desktop = self.smgr.createInstanceWithContext("com.sun.star.frame.Desktop", self.context)
        except Exception:
            self._stop_process()
            self._close_stderr()
            shutil.rmtree(self.profile_dir, ignore_errors=True)
            raise

    def _connect(self):
        local = uno.getComponentContext()
        resolver = local.ServiceManager.createInstanceWithContext("com.sun.star.bridge.UnoUrlResolver", local)
        target = f"uno:pipe,name={self.pipe_name};urp;StarOffice.ComponentContext"
        last_error: Exception | None = None
        for _ in range(80):
            if self.process.poll() is not None:
                raise RuntimeError(
                    f"LibreOffice exited during startup ({self.process.returncode}).{self._stderr_suffix()}"
                )
            try:
                return resolver.resolve(target)
            except Exception as exc:  # UNO raises bridge-specific exceptions
                last_error = exc
                time.sleep(0.1)
        raise RuntimeError(f"Timed out connecting to LibreOffice UNO pipe: {last_error}.{self._stderr_suffix()}")

    def open(self, path: str, read_only: bool) -> dict:
        if self.desktop is None:
            raise RuntimeError("LibreOffice desktop service is unavailable.")
        full = str(Path(path).expanduser().resolve())
        if not os.path.isfile(full):
            raise FileNotFoundError(full)
        document = self.desktop.loadComponentFromURL(
            uno.systemPathToFileUrl(full),
            "_blank",
            0,
            (
                prop("Hidden", True),
                prop("ReadOnly", bool(read_only)),
                prop("MacroExecutionMode", 0),
                prop("UpdateDocMode", 0),
            ),
        )
        if document is None:
            raise RuntimeError("LibreOffice did not return a spreadsheet document.")
        if not document.supportsService("com.sun.star.sheet.SpreadsheetDocument"):
            try:
                document.close(True)
            finally:
                raise ValueError("The selected document is not a Calc spreadsheet.")
        workbook_id = uuid.uuid4().hex
        self.documents[workbook_id] = document
        self.paths[workbook_id] = full
        return {"id": workbook_id, "path": full, "readOnly": bool(read_only)}

    def _doc(self, workbook_id: str):
        try:
            return self.documents[workbook_id]
        except KeyError as exc:
            raise KeyError(f"Unknown workbook '{workbook_id}'.") from exc

    @staticmethod
    def _sheet(document, name: str):
        sheets = document.getSheets()
        if not sheets.hasByName(name):
            raise KeyError(f"Workbook does not contain sheet '{name}'.")
        return sheets.getByName(name)

    @staticmethod
    def _range_request(sheet_name: str, address) -> dict:
        return {
            "sheet": sheet_name,
            "startRow": int(address.StartRow),
            "startColumn": int(address.StartColumn),
            "rowCount": int(address.EndRow - address.StartRow + 1),
            "columnCount": int(address.EndColumn - address.StartColumn + 1),
        }

    @staticmethod
    def _find_name_case_insensitive(names: tuple | list, requested: str) -> str | None:
        requested_folded = requested.casefold()
        for existing in names:
            text = str(existing)
            if text.casefold() == requested_folded:
                return text
        return None

    def _named_range_summary(self, document, name: str) -> dict | None:
        named_ranges = document.NamedRanges
        named = named_ranges.getByName(name)
        try:
            referred = named.getReferredCells()
        except Exception:
            return None
        if referred is None:
            return None
        try:
            address = referred.getRangeAddress()
        except Exception:
            return None
        sheet_names = document.getSheets().getElementNames()
        sheet_index = int(address.Sheet)
        if sheet_index < 0 or sheet_index >= len(sheet_names):
            return None
        sheet_name = str(sheet_names[sheet_index])
        return {"name": name, "range": self._range_request(sheet_name, address)}

    def list_sheets(self, workbook_id: str) -> list[dict]:
        document = self._doc(workbook_id)
        names = document.getSheets().getElementNames()
        return [{"name": str(name), "index": index} for index, name in enumerate(names)]

    def list_named_ranges(self, workbook_id: str) -> list[dict]:
        document = self._doc(workbook_id)
        names = [str(name) for name in document.NamedRanges.getElementNames()]
        summaries: list[dict] = []
        for name in sorted(names, key=str.casefold):
            summary = self._named_range_summary(document, name)
            if summary is not None:
                summaries.append(summary)
        return summaries

    def create_named_range(self, workbook_id: str, supplied_name: object, request: dict) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        name = named_range_name(supplied_name)
        sheet_name = str(request.get("sheet") or "").strip()
        start_row = int(request["startRow"])
        start_column = int(request["startColumn"])
        row_count = int(request["rowCount"])
        column_count = int(request["columnCount"])
        if not sheet_name:
            raise ValueError("Named range sheet is required.")
        if min(start_row, start_column) < 0 or row_count < 1 or column_count < 1:
            raise ValueError("Invalid named range coordinates.")
        if row_count > MAX_MATERIALIZED_ROWS or column_count > MAX_MATERIALIZED_COLUMNS:
            raise ValueError("Named range exceeds first-slice safety limits.")

        sheet = self._sheet(document, sheet_name)
        row_capacity = int(sheet.getRows().getCount())
        column_capacity = int(sheet.getColumns().getCount())
        if start_row + row_count > row_capacity or start_column + column_count > column_capacity:
            raise ValueError("Named range exceeds the sheet bounds.")

        named_ranges = document.NamedRanges
        existing_names = tuple(named_ranges.getElementNames())
        if self._find_name_case_insensitive(existing_names, name) is not None:
            raise ValueError(f"Workbook already contains a named range named '{name}'.")

        end_row = start_row + row_count - 1
        end_column = start_column + column_count - 1
        cell_range = sheet.getCellRangeByPosition(start_column, start_row, end_column, end_row)
        absolute_name = str(cell_range.AbsoluteName)
        reference_address = cell_range.getCellByPosition(0, 0).CellAddress
        named_ranges.addNewByName(name, absolute_name, reference_address, 0)
        summary = self._named_range_summary(document, name)
        if summary is None:
            try:
                named_ranges.removeByName(name)
            except Exception:
                pass
            raise RuntimeError("LibreOffice created a named expression that did not resolve to the requested cell range.")
        return summary

    def delete_named_range(self, workbook_id: str, supplied_name: object) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        requested = named_range_name(supplied_name)
        named_ranges = document.NamedRanges
        actual = self._find_name_case_insensitive(tuple(named_ranges.getElementNames()), requested)
        if actual is None:
            raise KeyError(f"Workbook does not contain named range '{requested}'.")
        if self._named_range_summary(document, actual) is None:
            raise PermissionError("Haven Data can delete only named ranges that resolve to one concrete cell range in this slice.")
        named_ranges.removeByName(actual)
        return {"ok": True}

    def read_range(self, workbook_id: str, request: dict) -> dict:
        document = self._doc(workbook_id)
        sheet_name = request["sheet"]
        start_row = int(request["startRow"])
        start_column = int(request["startColumn"])
        row_count = int(request["rowCount"])
        column_count = int(request["columnCount"])
        if min(start_row, start_column) < 0 or row_count < 1 or column_count < 1:
            raise ValueError("Invalid range coordinates.")
        if row_count > 1000 or column_count > 256:
            raise ValueError("Requested range exceeds first-slice safety limits.")
        sheet = self._sheet(document, sheet_name)
        values: list[list[str]] = []
        for row in range(start_row, start_row + row_count):
            current: list[str] = []
            for column in range(start_column, start_column + column_count):
                cell = sheet.getCellByPosition(column, row)
                current.append(cell.getString())
            values.append(current)
        return {
            "sheet": sheet_name,
            "startRow": start_row,
            "startColumn": start_column,
            "values": values,
        }

    def set_cell(self, workbook_id: str, address: dict, value: str, formula: str) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        sheet_name = address["sheet"]
        row = int(address["row"])
        column = int(address["column"])
        if row < 0 or column < 0:
            raise ValueError("Negative cell coordinates are invalid.")
        cell = self._sheet(document, sheet_name).getCellByPosition(column, row)
        if formula:
            cell.setFormula(formula if formula.startswith("=") else "=" + formula)
        else:
            try:
                number = float(value)
            except (TypeError, ValueError):
                cell.setString("" if value is None else str(value))
            else:
                cell.setValue(number)
        cell_formula = cell.getFormula()
        return {
            "address": {"sheet": sheet_name, "row": row, "column": column},
            "value": cell.getString(),
            "formula": cell_formula if cell_formula.startswith("=") else "",
        }

    def create_sheet_with_values(self, workbook_id: str, sheet_name: object, supplied_values: object) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        name = portable_sheet_name(sheet_name)
        if not isinstance(supplied_values, list) or not 1 <= len(supplied_values) <= MAX_MATERIALIZED_ROWS:
            raise ValueError(f"Materialized sheets must contain 1-{MAX_MATERIALIZED_ROWS} rows in this slice.")
        first_row = supplied_values[0]
        if not isinstance(first_row, list) or not 1 <= len(first_row) <= MAX_MATERIALIZED_COLUMNS:
            raise ValueError(f"Materialized sheets must contain 1-{MAX_MATERIALIZED_COLUMNS} columns.")
        column_count = len(first_row)

        values: list[list[str]] = []
        for supplied_row in supplied_values:
            if not isinstance(supplied_row, list) or len(supplied_row) != column_count:
                raise ValueError("Every materialized row must contain the same number of columns.")
            values.append(["" if value is None else str(value) for value in supplied_row])

        sheets = document.getSheets()
        existing_names = [str(item) for item in sheets.getElementNames()]
        if any(existing.casefold() == name.casefold() for existing in existing_names):
            raise ValueError(f"Workbook already contains a sheet named '{name}'.")

        inserted = False
        try:
            sheets.insertNewByName(name, sheets.getCount())
            inserted = True
            sheet = sheets.getByName(name)
            for row_index, row_values in enumerate(values):
                for column_index, value in enumerate(row_values):
                    # Query/database output is always written as literal text. A value such
                    # as '=1+1' must remain '=1+1' and never become a Calc formula.
                    sheet.getCellByPosition(column_index, row_index).setString(value)
            return self.read_range(
                workbook_id,
                {
                    "sheet": name,
                    "startRow": 0,
                    "startColumn": 0,
                    "rowCount": len(values),
                    "columnCount": column_count,
                },
            )
        except Exception:
            if inserted:
                try:
                    sheets.removeByName(name)
                except Exception:
                    pass
            raise

    def mutate_structure(self, workbook_id: str, sheet_name: object, axis: str, operation: str, index: object, count: object) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        name = str(sheet_name or "").strip()
        if not name:
            raise ValueError("sheet is required.")
        mutation_index = int(index)
        mutation_count = int(count)
        if mutation_index < 0:
            raise ValueError("Structural mutation index cannot be negative.")
        if not 1 <= mutation_count <= MAX_STRUCTURAL_MUTATION_COUNT:
            raise ValueError(f"Structural mutations must affect 1-{MAX_STRUCTURAL_MUTATION_COUNT} rows or columns at a time.")

        sheet = self._sheet(document, name)
        if axis == "rows":
            collection = sheet.getRows()
        elif axis == "columns":
            collection = sheet.getColumns()
        else:
            raise ValueError(f"Unsupported structural axis '{axis}'.")

        capacity = int(collection.getCount())
        if mutation_index >= capacity or mutation_index + mutation_count > capacity:
            raise ValueError(f"Structural mutation exceeds the sheet {axis} bounds.")
        if operation == "insert":
            collection.insertByIndex(mutation_index, mutation_count)
        elif operation == "delete":
            collection.removeByIndex(mutation_index, mutation_count)
        else:
            raise ValueError(f"Unsupported structural operation '{operation}'.")
        document.calculateAll()
        return {"ok": True}

    def recalculate(self, workbook_id: str) -> dict:
        document = self._doc(workbook_id)
        document.calculateAll()
        return {"ok": True}

    def save(self, workbook_id: str, destination_path: str) -> dict:
        document = self._doc(workbook_id)
        if bool(document.isReadonly()):
            raise PermissionError("Workbook was opened read-only.")
        destination = str(Path(destination_path).expanduser().resolve())
        source = self.paths[workbook_id]
        if os.path.normcase(destination) == os.path.normcase(source):
            raise PermissionError("The first slice only permits save-as to a new path; in-place overwrite is disabled.")
        Path(destination).parent.mkdir(parents=True, exist_ok=True)
        extension = Path(destination).suffix.lower()
        filters = {".ods": "calc8", ".xlsx": "Calc MS Excel 2007 XML"}
        if extension not in filters:
            raise ValueError("First slice supports only .ods and .xlsx save destinations.")
        document.storeAsURL(
            uno.systemPathToFileUrl(destination),
            (prop("FilterName", filters[extension]), prop("Overwrite", True)),
        )
        if not os.path.isfile(destination) or os.path.getsize(destination) == 0:
            raise IOError("LibreOffice did not produce a non-empty saved workbook.")
        self.paths[workbook_id] = destination
        return {"ok": True}

    def close(self, workbook_id: str) -> dict:
        document = self._doc(workbook_id)
        try:
            document.close(True)
        finally:
            self.documents.pop(workbook_id, None)
            self.paths.pop(workbook_id, None)
        return {"ok": True}

    def shutdown(self) -> None:
        for workbook_id in list(self.documents):
            try:
                self.close(workbook_id)
            except Exception:
                pass
        if self.desktop is not None:
            try:
                self.desktop.terminate()
            except Exception:
                pass
        self._stop_process()
        self._close_stderr()
        shutil.rmtree(self.profile_dir, ignore_errors=True)

    def _stop_process(self) -> None:
        if self.process.poll() is not None:
            return
        try:
            self.process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=3)

    def _stderr_suffix(self) -> str:
        try:
            self.stderr_file.flush()
            self.stderr_file.seek(0)
            text = self.stderr_file.read().strip()
            self.stderr_file.seek(0, os.SEEK_END)
        except Exception:
            return ""
        return f" LibreOffice stderr: {text[-4096:]}" if text else ""

    def _close_stderr(self) -> None:
        try:
            self.stderr_file.close()
        except Exception:
            pass


def serve() -> int:
    runtime = CalcRuntime()
    try:
        for line in sys.stdin:
            if not line.strip():
                continue
            request_id = 0
            try:
                request = json.loads(line)
                request_id = int(request.get("id", 0))
                method = request.get("method")
                params = request.get("params") or {}
                if method == "shutdown":
                    print(json.dumps({"id": request_id, "error": None, "result": {"ok": True}}), flush=True)
                    return 0
                if method == "open":
                    result = runtime.open(params["path"], bool(params.get("readOnly", False)))
                elif method == "listSheets":
                    result = runtime.list_sheets(params["workbookId"])
                elif method == "listNamedRanges":
                    result = runtime.list_named_ranges(params["workbookId"])
                elif method == "createNamedRange":
                    result = runtime.create_named_range(params["workbookId"], params["name"], params["range"])
                elif method == "deleteNamedRange":
                    result = runtime.delete_named_range(params["workbookId"], params["name"])
                elif method == "readRange":
                    result = runtime.read_range(params["workbookId"], params["range"])
                elif method == "setCell":
                    result = runtime.set_cell(params["workbookId"], params["address"], params.get("value", ""), params.get("formula", ""))
                elif method == "createSheetWithValues":
                    result = runtime.create_sheet_with_values(params["workbookId"], params["sheetName"], params["values"])
                elif method == "insertRows":
                    result = runtime.mutate_structure(params["workbookId"], params["sheet"], "rows", "insert", params["index"], params["count"])
                elif method == "deleteRows":
                    result = runtime.mutate_structure(params["workbookId"], params["sheet"], "rows", "delete", params["index"], params["count"])
                elif method == "insertColumns":
                    result = runtime.mutate_structure(params["workbookId"], params["sheet"], "columns", "insert", params["index"], params["count"])
                elif method == "deleteColumns":
                    result = runtime.mutate_structure(params["workbookId"], params["sheet"], "columns", "delete", params["index"], params["count"])
                elif method == "recalculate":
                    result = runtime.recalculate(params["workbookId"])
                elif method == "save":
                    result = runtime.save(params["workbookId"], params["destinationPath"])
                elif method == "close":
                    result = runtime.close(params["workbookId"])
                else:
                    raise ValueError(f"Unsupported method '{method}'.")
                print(json.dumps({"id": request_id, "error": None, "result": result}), flush=True)
            except Exception as exc:
                print(json.dumps({"id": request_id, "error": f"{type(exc).__name__}: {exc}", "result": None}), flush=True)
    finally:
        runtime.shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(serve())

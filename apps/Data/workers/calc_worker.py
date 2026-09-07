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


def prop(name: str, value):
    item = PropertyValue()
    item.Name = name
    item.Value = value
    return item


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

    def list_sheets(self, workbook_id: str) -> list[dict]:
        document = self._doc(workbook_id)
        names = document.getSheets().getElementNames()
        return [{"name": str(name), "index": index} for index, name in enumerate(names)]

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
                elif method == "readRange":
                    result = runtime.read_range(params["workbookId"], params["range"])
                elif method == "setCell":
                    result = runtime.set_cell(params["workbookId"], params["address"], params.get("value", ""), params.get("formula", ""))
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

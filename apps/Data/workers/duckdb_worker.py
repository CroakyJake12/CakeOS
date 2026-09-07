#!/usr/bin/env python3
"""Haven Data DuckDB worker.

First-slice worker: one local database, read-only SQL execution, no external access.
Requires the pinned DuckDB Python package at runtime.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

try:
    import duckdb
except Exception as exc:  # pragma: no cover - runtime dependency gate
    print(json.dumps({"id": 0, "error": f"duckdb is unavailable: {exc}", "result": None}), flush=True)
    raise SystemExit(78)


READ_ONLY_HEAD = re.compile(r"^\s*(SELECT|EXPLAIN)\b", re.IGNORECASE)
FORBIDDEN = re.compile(
    r"\b(ATTACH|DETACH|COPY|EXPORT|IMPORT|INSTALL|LOAD|CREATE|DROP|ALTER|INSERT|UPDATE|DELETE|MERGE|REPLACE|TRUNCATE|VACUUM|CALL|PRAGMA|SET)\b",
    re.IGNORECASE,
)
MEMORY_LIMIT = re.compile(r"^[1-9][0-9]*(?:\.[0-9]+)?\s*(?:KB|MB|GB)$", re.IGNORECASE)


class DuckDbRuntime:
    def __init__(self) -> None:
        self.connection = None
        self.path = None

    def open(self, database_path: str) -> dict:
        if self.connection is not None:
            self.close()
        if not database_path or not database_path.strip():
            raise ValueError("databasePath is required.")

        full = str(Path(database_path).expanduser().resolve())
        Path(full).parent.mkdir(parents=True, exist_ok=True)
        self.connection = duckdb.connect(full)
        self.path = full

        memory_limit = os.environ.get("HAVEN_DATA_DUCKDB_MEMORY_LIMIT", "512MB").strip()
        if not MEMORY_LIMIT.fullmatch(memory_limit):
            raise ValueError("HAVEN_DATA_DUCKDB_MEMORY_LIMIT must be a positive KB/MB/GB value.")
        threads = int(os.environ.get("HAVEN_DATA_DUCKDB_THREADS", "2"))
        if threads < 1 or threads > 8:
            raise ValueError("HAVEN_DATA_DUCKDB_THREADS must be between 1 and 8.")

        # Configure every capability before locking configuration. These settings are
        # defense in depth; the worker must still run inside an OS sandbox before ship.
        connection = self._conn()
        connection.execute("SET enable_external_access = false")
        connection.execute("SET autoinstall_known_extensions = false")
        connection.execute("SET autoload_known_extensions = false")
        connection.execute("SET allow_community_extensions = false")
        connection.execute("SET allow_persistent_secrets = false")
        connection.execute(f"SET memory_limit = '{memory_limit}'")
        connection.execute(f"SET threads = {threads}")
        connection.execute("SET lock_configuration = true")
        return {"ok": True}

    def _conn(self):
        if self.connection is None:
            raise RuntimeError("No DuckDB database is open.")
        return self.connection

    @staticmethod
    def _validate_read_only(sql: str) -> None:
        text = sql.strip()
        if not text:
            raise ValueError("SQL is empty.")
        if ";" in text.rstrip(";"):
            raise PermissionError("Multiple SQL statements are not allowed in the first slice.")
        if not READ_ONLY_HEAD.match(text):
            raise PermissionError("Only SELECT and EXPLAIN are allowed in the first slice.")
        if FORBIDDEN.search(text):
            raise PermissionError("SQL contains a statement or capability disabled by Haven Data.")

    def query(self, sql: str, max_rows: int) -> dict:
        self._validate_read_only(sql)
        if max_rows < 1 or max_rows > 1000:
            raise ValueError("maxRows must be between 1 and 1000.")
        cursor = self._conn().execute(sql)
        columns = [item[0] for item in cursor.description or []]
        fetched = cursor.fetchmany(max_rows + 1)
        truncated = len(fetched) > max_rows
        rows = fetched[:max_rows]
        return {
            "columns": columns,
            "rows": [["" if value is None else str(value) for value in row] for row in rows],
            "truncated": truncated,
        }

    def close(self) -> dict:
        if self.connection is not None:
            self.connection.close()
        self.connection = None
        self.path = None
        return {"ok": True}


def serve() -> int:
    runtime = DuckDbRuntime()
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
                    result = runtime.open(params["databasePath"])
                elif method == "query":
                    result = runtime.query(params["sql"], int(params.get("maxRows", 200)))
                elif method == "close":
                    result = runtime.close()
                else:
                    raise ValueError(f"Unsupported method '{method}'.")
                print(json.dumps({"id": request_id, "error": None, "result": result}), flush=True)
            except Exception as exc:
                print(json.dumps({"id": request_id, "error": f"{type(exc).__name__}: {exc}", "result": None}), flush=True)
    finally:
        runtime.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(serve())

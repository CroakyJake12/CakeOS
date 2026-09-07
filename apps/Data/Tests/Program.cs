using HavenOS.Apps.Data;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var address = new DataCellAddress("Sheet 1", 0, 1);
var cellSnapshot = new DataCellSnapshot(address, "42", "=SUM(A1:A2)");
Assert(cellSnapshot.Address.Sheet == "Sheet 1", "Cell address sheet was not retained.");
Assert(cellSnapshot.Address.Row == 0 && cellSnapshot.Address.Column == 1, "Cell coordinates were not retained.");
Assert(cellSnapshot.Formula == "=SUM(A1:A2)", "Formula was not retained.");

var range = new DataRangeRequest("Sheet 1", 0, 0, 10, 8);
Assert(range.RowCount == 10 && range.ColumnCount == 8, "P1 grid shape changed unexpectedly.");

var queryResult = new DataQueryResult(["total"], [["42"]], false);
Assert(queryResult.Columns.Count == 1 && queryResult.Rows.Count == 1, "Query result contract is invalid.");

var fake = new FakeSpreadsheetEngine();
await using var fakeDatabase = new FakeDatabaseEngine();
await fakeDatabase.OpenAsync("fixture.duckdb");
await using (var session = new DataGridSession(fake))
{
    var opened = await session.OpenAsync("fixture.ods");
    Assert(opened.Grid.Values.Count == DataGridSession.VisibleRows, "Grid session did not request 10 visible rows.");
    Assert(opened.Grid.Values.All(row => row.Count == DataGridSession.VisibleColumns), "Grid session did not request 8 visible columns.");
    Assert(opened.ActiveSheet.Name == "Sheet 1", "Grid session did not select the first discovered sheet.");

    var edited = await session.EditCellAsync(0, 0, "5");
    Assert(edited.Grid.Values[0][0] == "5", "Grid session did not refresh after a cell edit.");
    Assert(fake.RecalculateCalls == 1, "Grid session did not request recalculation after edit.");

    var formula = await session.EditCellAsync(0, 1, string.Empty, "=A1*2");
    Assert(formula.Grid.Values[0][1] == "10", "Grid session did not expose the recalculated formula result.");
    Assert(fake.RecalculateCalls == 2, "Grid session did not recalculate the formula edit.");

    var bridge = new DataWorkbookDatabaseBridge(fake, fakeDatabase);
    var published = await bridge.PublishRangeAsync(
        opened.Workbook.Id,
        new DataRangeRequest("Sheet 1", 0, 0, 1, 2),
        "GridSnapshot");
    Assert(published.Columns.SequenceEqual(["A", "B"]), "Database bridge did not generate stable spreadsheet column names.");
    Assert(published.RowCount == 1, "Database bridge published the wrong row count.");
    Assert(fakeDatabase.LastTable is not null, "Database bridge did not call the database engine.");
    Assert(fakeDatabase.LastTable.Rows[0].SequenceEqual(["5", "10"]), "Database bridge did not publish displayed spreadsheet values.");

    var secondSheet = await session.SelectSheetAsync(1);
    Assert(secondSheet.ActiveSheet.Name == "Summary", "Grid session sheet selection failed.");

    await session.SaveAsAsync("saved.ods");
    Assert(fake.LastSavePath == "saved.ods", "Grid session did not delegate save-as.");
    await session.CloseAsync();
    Assert(fake.CloseCalls == 1, "Grid session did not close the workbook exactly once.");
}
await fakeDatabase.CloseAsync();

Console.WriteLine("Haven Data contract, grid-session and database-bridge smoke checks passed.");

internal sealed class FakeSpreadsheetEngine : IDataSpreadsheetEngine
{
    private readonly Dictionary<(string Sheet, int Row, int Column), string> _values = new();
    private DataWorkbookHandle? _open;

    public int RecalculateCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public string? LastSavePath { get; private set; }

    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _open = new DataWorkbookHandle("fake-workbook", path, readOnly);
        return Task.FromResult(_open);
    }

    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        IReadOnlyList<DataSheetSummary> sheets = [new("Sheet 1", 0), new("Summary", 1)];
        return Task.FromResult(sheets);
    }

    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        var rows = new List<IReadOnlyList<string>>(range.RowCount);
        for (var row = 0; row < range.RowCount; row++)
        {
            var values = new List<string>(range.ColumnCount);
            for (var column = 0; column < range.ColumnCount; column++)
                values.Add(_values.GetValueOrDefault((range.Sheet, range.StartRow + row, range.StartColumn + column), string.Empty));
            rows.Add(values);
        }
        return Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn, rows));
    }

    public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        var stored = string.IsNullOrWhiteSpace(formula) ? value ?? string.Empty : formula == "=A1*2" ? "10" : value ?? string.Empty;
        _values[(address.Sheet, address.Row, address.Column)] = stored;
        return Task.FromResult(new DataCellSnapshot(address, stored, formula ?? string.Empty));
    }

    public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        RecalculateCalls++;
        return Task.CompletedTask;
    }

    public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        LastSavePath = destinationPath;
        return Task.CompletedTask;
    }

    public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        CloseCalls++;
        _open = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _open = null;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen(string workbookId)
    {
        if (_open is null || !string.Equals(_open.Id, workbookId, StringComparison.Ordinal))
            throw new InvalidOperationException("Fake workbook is not open.");
    }
}

internal sealed class FakeDatabaseEngine : IDataDatabaseEngine
{
    private bool _open;

    public DataTableSnapshot? LastTable { get; private set; }

    public Task OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _open = true;
        return Task.CompletedTask;
    }

    public Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        LastTable = table;
        return Task.CompletedTask;
    }

    public Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        return Task.FromResult(new DataQueryResult([], [], false));
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _open = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _open = false;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (!_open)
            throw new InvalidOperationException("Fake database is not open.");
    }
}

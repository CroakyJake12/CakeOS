namespace HavenOS.Apps.Data;

public sealed record DataCellAddress(string Sheet, int Row, int Column);
public sealed record DataCellSnapshot(DataCellAddress Address, string Value, string Formula = "");
public sealed record DataRangeRequest(string Sheet, int StartRow, int StartColumn, int RowCount, int ColumnCount);
public sealed record DataRangeSnapshot(string Sheet, int StartRow, int StartColumn, IReadOnlyList<IReadOnlyList<string>> Values);
public sealed record DataSheetSummary(string Name, int Index);
public sealed record DataWorkbookHandle(string Id, string Path, bool ReadOnly);
public sealed record DataTableSnapshot(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
public sealed record DataPublishedTable(string Name, IReadOnlyList<string> Columns, int RowCount, DataRangeRequest SourceRange);
public sealed record DataQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated);
public sealed record DataMaterializedQueryResult(string Sheet, DataRangeRequest Range, int DataRowCount);

public interface IDataSpreadsheetEngine : IAsyncDisposable
{
    Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default);
    Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default);
    Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default);
    Task<DataRangeSnapshot> CreateSheetWithValuesAsync(string workbookId, string sheetName, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken cancellationToken = default);
    Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default);
    Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default);
    Task CloseAsync(string workbookId, CancellationToken cancellationToken = default);
}

public interface IDataDatabaseEngine : IAsyncDisposable
{
    Task OpenAsync(string databasePath, CancellationToken cancellationToken = default);
    Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default);
    Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public sealed class DataAppService(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
{
    public IDataSpreadsheetEngine Spreadsheet { get; } = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
    public IDataDatabaseEngine Database { get; } = database ?? throw new ArgumentNullException(nameof(database));
}

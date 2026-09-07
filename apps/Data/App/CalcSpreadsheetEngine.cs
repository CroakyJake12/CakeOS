namespace HavenOS.Apps.Data;

public sealed class CalcSpreadsheetEngine : IDataSpreadsheetEngine
{
    private const int MaximumMaterializedRows = 1001;
    private const int MaximumMaterializedColumns = 256;

    private readonly JsonLineWorkerClient _worker;

    public CalcSpreadsheetEngine(string workerScriptPath, string pythonExecutable = "python3")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerScriptPath);
        _worker = new JsonLineWorkerClient(
            pythonExecutable,
            [workerScriptPath],
            new Dictionary<string, string?>
            {
                ["PYTHONUNBUFFERED"] = "1"
            });
    }

    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _worker.CallAsync<DataWorkbookHandle>("open", new { path, readOnly }, cancellationToken);
    }

    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        return _worker.CallAsync<IReadOnlyList<DataSheetSummary>>("listSheets", new { workbookId }, cancellationToken);
    }

    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(range);
        return _worker.CallAsync<DataRangeSnapshot>("readRange", new { workbookId, range }, cancellationToken);
    }

    public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(address);
        return _worker.CallAsync<DataCellSnapshot>("setCell", new { workbookId, address, value = value ?? string.Empty, formula = formula ?? string.Empty }, cancellationToken);
    }

    public Task<DataRangeSnapshot> CreateSheetWithValuesAsync(
        string workbookId,
        string sheetName,
        IReadOnlyList<IReadOnlyList<string>> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidatePortableSheetName(sheetName);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > MaximumMaterializedRows)
            throw new ArgumentOutOfRangeException(nameof(values), $"Materialized sheets must contain 1-{MaximumMaterializedRows} rows in this slice.");
        var columns = values[0].Count;
        if (columns is < 1 or > MaximumMaterializedColumns)
            throw new ArgumentOutOfRangeException(nameof(values), $"Materialized sheets must contain 1-{MaximumMaterializedColumns} columns.");
        if (values.Any(row => row.Count != columns))
            throw new ArgumentException("Every materialized row must contain the same number of columns.", nameof(values));

        return _worker.CallAsync<DataRangeSnapshot>(
            "createSheetWithValues",
            new { workbookId, sheetName = sheetName.Trim(), values },
            cancellationToken);
    }

    public async Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        _ = await _worker.CallAsync<WorkerAck>("recalculate", new { workbookId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        _ = await _worker.CallAsync<WorkerAck>("save", new { workbookId, destinationPath }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        _ = await _worker.CallAsync<WorkerAck>("close", new { workbookId }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private static void ValidatePortableSheetName(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        var trimmed = sheetName.Trim();
        if (trimmed.Length > 31)
            throw new ArgumentException("Materialized sheet names must be at most 31 characters for ODS/XLSX portability.", nameof(sheetName));
        if (trimmed.Any(character => char.IsControl(character) || "[]:*?/\\".Contains(character)))
            throw new ArgumentException("Materialized sheet names contain a character that is unsafe for ODS/XLSX portability.", nameof(sheetName));
        if (trimmed.StartsWith('\'') || trimmed.EndsWith('\''))
            throw new ArgumentException("Materialized sheet names cannot begin or end with an apostrophe.", nameof(sheetName));
    }

    private sealed record WorkerAck(bool Ok);
}

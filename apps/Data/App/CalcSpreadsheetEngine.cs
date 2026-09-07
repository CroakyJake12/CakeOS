namespace HavenOS.Apps.Data;

public sealed class CalcSpreadsheetEngine : IDataSpreadsheetEngine
{
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

    private sealed record WorkerAck(bool Ok);
}

namespace HavenOS.Apps.Data;

public sealed class DuckDbDatabaseEngine : IDataDatabaseEngine
{
    private readonly JsonLineWorkerClient _worker;

    public DuckDbDatabaseEngine(string workerScriptPath, string pythonExecutable = "python3")
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

    public async Task OpenAsync(string databasePath, CancellationToken cancellationToken = default) =>
        _ = await _worker.CallAsync<WorkerAck>("open", new { databasePath }, cancellationToken).ConfigureAwait(false);

    public Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        return _worker.CallAsync<DataQueryResult>("query", new { sql, maxRows }, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default) =>
        _ = await _worker.CallAsync<WorkerAck>("close", null, cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private sealed record WorkerAck(bool Ok);
}

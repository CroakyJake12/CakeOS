namespace HavenOS.Apps.Data;

public sealed record DataQueryExecution(
    string Sql,
    int MaxRows,
    DataQueryResult Result,
    DateTimeOffset ExecutedAtUtc);

public sealed record DataQuerySessionSnapshot(
    string DatabasePath,
    IReadOnlyList<DataPublishedTable> PublishedTables,
    IReadOnlyList<DataQueryExecution> RecentQueries);

/// <summary>
/// HUI/GenUI-facing query workflow. It exposes only typed publication plus bounded
/// read-only SQL execution; schema mutation remains inaccessible through query text.
/// </summary>
public sealed class DataQuerySession : IAsyncDisposable
{
    private const int MaximumRecentQueries = 20;

    private readonly IDataDatabaseEngine _database;
    private readonly DataWorkbookDatabaseBridge _bridge;
    private readonly List<DataPublishedTable> _publishedTables = [];
    private readonly List<DataQueryExecution> _recentQueries = [];
    private string? _databasePath;
    private bool _disposed;

    public DataQuerySession(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
    }

    public bool IsOpen => _databasePath is not null;

    public async Task<DataQuerySessionSnapshot> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (_databasePath is not null)
            throw new InvalidOperationException("A database is already open in this Data query session.");

        await _database.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        _databasePath = databasePath;
        _publishedTables.Clear();
        _recentQueries.Clear();
        return Snapshot();
    }

    public async Task<DataPublishedTable> PublishRangeAsync(
        string workbookId,
        DataRangeRequest sourceRange,
        string tableName,
        bool firstRowIsHeaders = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        var published = await _bridge.PublishRangeAsync(
            workbookId,
            sourceRange,
            tableName,
            firstRowIsHeaders,
            cancellationToken).ConfigureAwait(false);

        var index = _publishedTables.FindIndex(table =>
            string.Equals(table.Name, published.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _publishedTables[index] = published;
        else
            _publishedTables.Add(published);
        return published;
    }

    public async Task<DataQueryExecution> ExecuteAsync(
        string sql,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxRows));

        var result = await _database.ExecuteReadOnlyAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);
        var execution = new DataQueryExecution(sql.Trim(), maxRows, result, DateTimeOffset.UtcNow);
        _recentQueries.Insert(0, execution);
        if (_recentQueries.Count > MaximumRecentQueries)
            _recentQueries.RemoveRange(MaximumRecentQueries, _recentQueries.Count - MaximumRecentQueries);
        return execution;
    }

    public DataQuerySessionSnapshot Snapshot()
    {
        ThrowIfDisposed();
        var path = EnsureOpen();
        return new DataQuerySessionSnapshot(path, _publishedTables.ToArray(), _recentQueries.ToArray());
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_databasePath is null)
            return;

        _databasePath = null;
        _publishedTables.Clear();
        _recentQueries.Clear();
        await _database.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_databasePath is not null)
        {
            _databasePath = null;
            _publishedTables.Clear();
            _recentQueries.Clear();
            try { await _database.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }

    private string EnsureOpen() =>
        _databasePath ?? throw new InvalidOperationException("No database is open in this Data query session.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

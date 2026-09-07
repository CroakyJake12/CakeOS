using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

/// <summary>
/// Thin application-session boundary for Haven Boards.
///
/// It owns no network behavior: opening, mutation, and reopen all flow through the supplied
/// <see cref="IHavenBoardStore"/>. HUI commands are serialized, persisted before becoming the
/// current snapshot, and then projected back into the scene.
/// </summary>
public sealed class HavenBoardsHuiSession : IAsyncDisposable
{
    private readonly IHavenBoardStore _store;
    private readonly string _boardId;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _queueLock = new();
    private Task _queuedSceneCommands = Task.CompletedTask;
    private bool _disposed;

    private HavenBoardsHuiSession(
        IHavenBoardStore store,
        string boardId,
        HavenBoardSnapshot snapshot,
        bool created)
    {
        _store = store;
        _boardId = boardId;
        Snapshot = snapshot;
        Scene = new HavenBoardsHuiScene();
        Scene.SetSnapshot(snapshot);
        Scene.SetStatus(created ? "Created locally" : "Loaded locally");
        Scene.CommandRequested += OnSceneCommandRequested;
    }

    public HavenBoardsHuiScene Scene { get; }
    public HavenBoardSnapshot Snapshot { get; private set; }

    public static async Task<HavenBoardsHuiSession> OpenAsync(
        IHavenBoardStore store,
        string boardId = "board-main",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(boardId))
            throw new ArgumentException("A board ID is required.", nameof(boardId));

        var snapshot = await store.LoadAsync(boardId, cancellationToken).ConfigureAwait(false);
        var created = snapshot is null;
        if (snapshot is null)
        {
            snapshot = HavenBoardSnapshot.CreateDefault() with { Id = boardId };
            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        return new HavenBoardsHuiSession(store, boardId, snapshot, created);
    }

    public async Task<HavenBoardSnapshot> ExecuteAsync(
        HavenBoardCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(command);

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updated = HavenBoardReducer.Apply(Snapshot, command);
            if (!string.Equals(updated.Id, _boardId, StringComparison.Ordinal))
                throw new InvalidOperationException("The reducer changed the open board identity.");

            // Persist first. If this fails, the in-memory/visible snapshot remains the last durable one.
            await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            Snapshot = updated;
            Scene.SetSnapshot(updated);
            Scene.SetStatus("Saved locally");
            return updated;
        }
        catch
        {
            Scene.SetStatus("Local save failed");
            throw;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>Waits until every HUI command queued before this call has completed.</summary>
    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        Task pending;
        lock (_queueLock)
            pending = _queuedSceneCommands;

        await pending.ConfigureAwait(false);
    }

    private void OnSceneCommandRequested(object? sender, HavenBoardCommand command)
    {
        lock (_queueLock)
        {
            var preceding = _queuedSceneCommands;
            _queuedSceneCommands = RunQueuedSceneCommandAsync(preceding, command);
        }
    }

    private async Task RunQueuedSceneCommandAsync(Task preceding, HavenBoardCommand command)
    {
        try
        {
            await preceding.ConfigureAwait(false);
        }
        catch
        {
            // A previous UI command already surfaced its own failure. Preserve ordering while allowing
            // a later command to retry against the last durable snapshot.
        }

        await ExecuteAsync(command).ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        Task pending;
        lock (_queueLock)
            pending = _queuedSceneCommands;

        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            Scene.CommandRequested -= OnSceneCommandRequested;
            Scene.Dispose();
            _mutationGate.Dispose();
        }
    }
}

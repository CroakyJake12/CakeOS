namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Converts the interaction callbacks exposed by AppFlowy Board into Haven-owned
/// commands. Product/HUI code depends on these commands rather than Flutter or
/// AppFlowy types.
/// </summary>
public static class AppFlowyBoardEventAdapter
{
    public static MoveGroupCommand MoveGroup(int fromIndex, int toIndex) =>
        new(fromIndex, toIndex);

    public static MoveCardCommand MoveCardWithinGroup(string groupId, int fromIndex, int toIndex) =>
        new(groupId, fromIndex, groupId, toIndex);

    public static MoveCardCommand MoveCardBetweenGroups(
        string fromGroupId,
        int fromIndex,
        string toGroupId,
        int toIndex) =>
        new(fromGroupId, fromIndex, toGroupId, toIndex);
}

public interface IHavenBoardStore
{
    Task<HavenBoardSnapshot?> LoadAsync(string boardId, CancellationToken cancellationToken = default);
    Task SaveAsync(HavenBoardSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IHavenBoardCommandSink
{
    Task<HavenBoardSnapshot> ExecuteAsync(HavenBoardCommand command, CancellationToken cancellationToken = default);
}

public sealed class HavenBoardCommandService(IHavenBoardStore store, string boardId = "board-main") : IHavenBoardCommandSink
{
    public async Task<HavenBoardSnapshot> ExecuteAsync(
        HavenBoardCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await store.LoadAsync(boardId, cancellationToken).ConfigureAwait(false)
            ?? HavenBoardSnapshot.CreateDefault();
        var updated = HavenBoardReducer.Apply(current, command);
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }
}

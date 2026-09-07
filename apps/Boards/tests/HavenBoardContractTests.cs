using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenBoardReducerTests
{
    [Fact]
    public void Move_within_group_matches_appflowy_remove_then_insert_semantics()
    {
        var snapshot = new HavenBoardSnapshot(
            "board-main",
            "Board",
            7,
            [new HavenBoardGroup(
                "todo",
                "To do",
                [
                    new HavenBoardCard("a", "A"),
                    new HavenBoardCard("b", "B"),
                    new HavenBoardCard("c", "C")
                ])]);

        var updated = HavenBoardReducer.Apply(
            snapshot,
            AppFlowyBoardEventAdapter.MoveCardWithinGroup("todo", 0, 1));

        Assert.Equal(["b", "a", "c"], updated.Groups[0].Cards.Select(card => card.Id).ToArray());
        Assert.Equal(8, updated.Version);
    }

    [Fact]
    public void Move_between_groups_inserts_at_requested_appflowy_index()
    {
        var snapshot = new HavenBoardSnapshot(
            "board-main",
            "Board",
            1,
            [
                new HavenBoardGroup("todo", "To do", [new HavenBoardCard("a", "A")]),
                new HavenBoardGroup("done", "Done", [new HavenBoardCard("b", "B")])
            ]);

        var updated = HavenBoardReducer.Apply(
            snapshot,
            AppFlowyBoardEventAdapter.MoveCardBetweenGroups("todo", 0, "done", 1));

        Assert.Empty(updated.Groups[0].Cards);
        Assert.Equal(["b", "a"], updated.Groups[1].Cards.Select(card => card.Id).ToArray());
    }

    [Fact]
    public void Move_group_reorders_without_changing_card_identity()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var cardIds = snapshot.Groups.SelectMany(group => group.Cards).Select(card => card.Id).Order().ToArray();

        var updated = HavenBoardReducer.Apply(snapshot, AppFlowyBoardEventAdapter.MoveGroup(0, 2));

        Assert.Equal("doing", updated.Groups[0].Id);
        Assert.Equal("done", updated.Groups[1].Id);
        Assert.Equal("todo", updated.Groups[2].Id);
        Assert.Equal(cardIds, updated.Groups.SelectMany(group => group.Cards).Select(card => card.Id).Order().ToArray());
    }

    [Fact]
    public void Duplicate_card_ids_are_rejected()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new CreateCardCommand("todo", "card-2", "Duplicate")));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class JsonFileHavenBoardStoreTests
{
    [Fact]
    public async Task Round_trip_preserves_order_hierarchy_and_attachments()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var snapshot = new HavenBoardSnapshot(
                "board-main",
                "Local board",
                42,
                [new HavenBoardGroup(
                    "todo",
                    "To do",
                    [new HavenBoardCard(
                        "child",
                        "Nested",
                        ParentCardId: "parent",
                        Attachments:
                        [
                            new HavenBoardAttachment(
                                "attachment-1",
                                "brief.txt",
                                "attachments/attachment-1",
                                HavenBoardAttachmentAvailability.Available)
                        ])])]);

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync("board-main");

            Assert.NotNull(loaded);
            Assert.Equal(snapshot, loaded);
        });
    }

    [Fact]
    public async Task Corrupt_primary_recovers_previous_durable_backup()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var first = HavenBoardSnapshot.CreateDefault() with { Title = "Previous", Version = 10 };
            var second = first with { Title = "Current", Version = 11 };

            await store.SaveAsync(first);
            await store.SaveAsync(second);
            await File.WriteAllTextAsync(Path.Combine(root, "board-main.json"), "{ not-valid-json");

            var loaded = await store.LoadAsync("board-main");

            Assert.NotNull(loaded);
            Assert.Equal("Previous", loaded.Title);
            Assert.Equal(10, loaded.Version);
        });
    }

    [Fact]
    public async Task Unsafe_board_id_is_rejected_before_path_resolution()
    {
        await WithStoreAsync(async (store, _) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../outside"));
        });
    }

    [Fact]
    public async Task Command_service_reduces_then_persists()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var service = new HavenBoardCommandService(store);

            var updated = await service.ExecuteAsync(new CreateCardCommand("todo", "card-4", "Persist me"));
            var reloaded = await store.LoadAsync("board-main");

            Assert.NotNull(reloaded);
            Assert.Equal(updated, reloaded);
            Assert.Contains(reloaded.Groups[0].Cards, card => card.Id == "card-4" && card.Title == "Persist me");
        });
    }

    private static async Task WithStoreAsync(Func<JsonFileHavenBoardStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-boards-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await test(store, root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Test cleanup must not hide the tested assertion result.
            }
            catch (UnauthorizedAccessException)
            {
                // Test cleanup must not hide the tested assertion result.
            }
        }
    }
}

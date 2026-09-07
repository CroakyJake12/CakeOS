using System.Text.Json.Serialization;

namespace CakeOS.Apps.Boards.Contract;

public sealed record HavenBoardSnapshot(
    string Id,
    string Title,
    long Version,
    IReadOnlyList<HavenBoardGroup> Groups)
{
    public static HavenBoardSnapshot CreateDefault() => new(
        Id: "board-main",
        Title: "Haven Boards",
        Version: 1,
        Groups:
        [
            new HavenBoardGroup("todo", "To do", [new HavenBoardCard("card-1", "First task")]),
            new HavenBoardGroup("doing", "Doing", [new HavenBoardCard("card-2", "Try AppFlowy Board")]),
            new HavenBoardGroup("done", "Done", [new HavenBoardCard("card-3", "Persist locally")])
        ]);
}

public sealed record HavenBoardGroup(
    string Id,
    string Title,
    IReadOnlyList<HavenBoardCard> Cards);

public sealed record HavenBoardCard(
    string Id,
    string Title,
    string? ParentCardId = null,
    IReadOnlyList<HavenBoardAttachment>? Attachments = null);

public sealed record HavenBoardAttachment(
    string Id,
    string DisplayName,
    string LocalReference,
    HavenBoardAttachmentAvailability Availability = HavenBoardAttachmentAvailability.Available);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HavenBoardAttachmentAvailability
{
    Available,
    Missing,
    RemoteOnly
}

public abstract record HavenBoardCommand;

public sealed record CreateCardCommand(string GroupId, string CardId, string Title) : HavenBoardCommand;
public sealed record RenameGroupCommand(string GroupId, string Title) : HavenBoardCommand;
public sealed record MoveGroupCommand(int FromIndex, int ToIndex) : HavenBoardCommand;
public sealed record MoveCardCommand(string FromGroupId, int FromIndex, string ToGroupId, int ToIndex) : HavenBoardCommand;
public sealed record AddAttachmentCommand(string CardId, HavenBoardAttachment Attachment) : HavenBoardCommand;
public sealed record RemoveAttachmentCommand(string CardId, string AttachmentId) : HavenBoardCommand;

public static class HavenBoardReducer
{
    public static HavenBoardSnapshot Apply(HavenBoardSnapshot snapshot, HavenBoardCommand command)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command);

        var groups = snapshot.Groups
            .Select(group => new MutableGroup(group.Id, group.Title, group.Cards.ToList()))
            .ToList();

        switch (command)
        {
            case CreateCardCommand create:
            {
                var target = FindGroup(groups, create.GroupId);
                if (groups.SelectMany(group => group.Cards).Any(card => card.Id == create.CardId))
                    throw new InvalidOperationException($"Card '{create.CardId}' already exists.");
                target.Cards.Add(new HavenBoardCard(create.CardId, NormaliseTitle(create.Title, "Untitled card")));
                break;
            }
            case RenameGroupCommand rename:
            {
                var target = FindGroup(groups, rename.GroupId);
                target.Title = NormaliseTitle(rename.Title, "Untitled group");
                break;
            }
            case MoveGroupCommand moveGroup:
            {
                RequireIndex(moveGroup.FromIndex, groups.Count, nameof(moveGroup.FromIndex));
                RequireInsertIndex(moveGroup.ToIndex, groups.Count, nameof(moveGroup.ToIndex));
                if (moveGroup.FromIndex != moveGroup.ToIndex)
                {
                    var moved = groups[moveGroup.FromIndex];
                    groups.RemoveAt(moveGroup.FromIndex);
                    groups.Insert(Math.Clamp(moveGroup.ToIndex, 0, groups.Count), moved);
                }
                break;
            }
            case MoveCardCommand moveCard:
            {
                var source = FindGroup(groups, moveCard.FromGroupId);
                var target = FindGroup(groups, moveCard.ToGroupId);
                RequireIndex(moveCard.FromIndex, source.Cards.Count, nameof(moveCard.FromIndex));

                var card = source.Cards[moveCard.FromIndex];
                source.Cards.RemoveAt(moveCard.FromIndex);
                target.Cards.Insert(Math.Clamp(moveCard.ToIndex, 0, target.Cards.Count), card);
                break;
            }
            case AddAttachmentCommand addAttachment:
            {
                ArgumentNullException.ThrowIfNull(addAttachment.Attachment);
                var located = FindCard(groups, addAttachment.CardId);
                var attachments = (located.Card.Attachments ?? []).ToList();
                if (attachments.Any(attachment => string.Equals(
                        attachment.Id,
                        addAttachment.Attachment.Id,
                        StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        $"Attachment '{addAttachment.Attachment.Id}' already exists on card '{addAttachment.CardId}'.");

                attachments.Add(addAttachment.Attachment);
                located.Group.Cards[located.Index] = located.Card with { Attachments = attachments.ToArray() };
                break;
            }
            case RemoveAttachmentCommand removeAttachment:
            {
                var located = FindCard(groups, removeAttachment.CardId);
                var attachments = (located.Card.Attachments ?? []).ToList();
                var removed = attachments.RemoveAll(attachment => string.Equals(
                    attachment.Id,
                    removeAttachment.AttachmentId,
                    StringComparison.Ordinal));
                if (removed == 0)
                    throw new InvalidOperationException(
                        $"Attachment '{removeAttachment.AttachmentId}' does not exist on card '{removeAttachment.CardId}'.");

                located.Group.Cards[located.Index] = located.Card with { Attachments = attachments.ToArray() };
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported board command '{command.GetType().Name}'.");
        }

        return snapshot with
        {
            Version = checked(snapshot.Version + 1),
            Groups = groups.Select(group => new HavenBoardGroup(group.Id, group.Title, group.Cards.ToArray())).ToArray()
        };
    }

    private static MutableGroup FindGroup(IReadOnlyList<MutableGroup> groups, string id) =>
        groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Board group '{id}' does not exist.");

    private static LocatedCard FindCard(IReadOnlyList<MutableGroup> groups, string cardId)
    {
        foreach (var group in groups)
        {
            var index = group.Cards.FindIndex(card => string.Equals(card.Id, cardId, StringComparison.Ordinal));
            if (index >= 0)
                return new LocatedCard(group, index, group.Cards[index]);
        }

        throw new InvalidOperationException($"Board card '{cardId}' does not exist.");
    }

    private static void RequireIndex(int index, int count, string name)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(name, index, $"Expected an index from 0 to {Math.Max(0, count - 1)}.");
    }

    private static void RequireInsertIndex(int index, int count, string name)
    {
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(name, index, $"Expected an insertion index from 0 to {count}.");
    }

    private static string NormaliseTitle(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record LocatedCard(MutableGroup Group, int Index, HavenBoardCard Card);

    private sealed class MutableGroup(string id, string title, List<HavenBoardCard> cards)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = title;
        public List<HavenBoardCard> Cards { get; } = cards;
    }
}

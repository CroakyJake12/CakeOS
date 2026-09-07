using CakeOS.Apps.Boards.Contract;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace CakeOS.Apps.Boards.Hui;

public delegate void HavenBoardCommandRequestedHandler(object? sender, HavenBoardCommand command);

/// <summary>
/// HUI-only projection of the neutral Haven Boards snapshot.
///
/// The scene intentionally has no Flutter/AppFlowy or Avalonia dependency. AppFlowy Board remains
/// behind the adapter/proof boundary; product rendering is performed by Haven UI primitives.
/// </summary>
public sealed class HavenBoardsHuiScene : IDisposable
{
    private readonly List<HavenButton> _wiredButtons = [];
    private HavenBoardSnapshot _snapshot = HavenBoardSnapshot.CreateDefault();
    private bool _disposed;

    public HavenBoardsHuiScene()
    {
        Root = BuildRoot();
        BoardLanes = Get<Container>("BoardLanes");
        BoardTitle = Get<HavenText>("BoardTitle");
        Status = Get<HavenText>("Status");
    }

    public Page Root { get; }
    public Container BoardLanes { get; }
    public HavenText BoardTitle { get; }
    public HavenText Status { get; }

    /// <summary>Raised for mutations. The owner validates, persists, then calls <see cref="SetSnapshot"/>.</summary>
    public event HavenBoardCommandRequestedHandler? CommandRequested;

    public void SetSnapshot(HavenBoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();

        _snapshot = snapshot;
        BoardTitle.Content = snapshot.Title;
        RebuildBoard();
    }

    public void SetStatus(string? status)
    {
        ThrowIfDisposed();
        Status.Content = status ?? string.Empty;
        Status.SetValue(
            HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void RebuildBoard()
    {
        UnwireButtons();
        foreach (var child in BoardLanes.Children.ToArray())
            BoardLanes.Remove(child);

        if (_snapshot.Groups.Count == 0)
        {
            var empty = new HavenText { Content = "This board has no groups yet." };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            BoardLanes.Add(empty);
            return;
        }

        for (var groupIndex = 0; groupIndex < _snapshot.Groups.Count; groupIndex++)
            BoardLanes.Add(BuildLane(_snapshot.Groups[groupIndex], groupIndex));
    }

    private Container BuildLane(HavenBoardGroup group, int groupIndex)
    {
        var lane = new Container { Layout = HavenLayout.Vertical };
        lane.Name = $"BoardLane_{SafeName(group.Id)}";
        lane.SetValue(HavenProperties.Width, HavenLength.Px(300));
        lane.SetValue(HavenProperties.MinWidth, HavenLength.Px(300));
        lane.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(12)));
        lane.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        lane.SetValue(HavenProperties.Background, "SurfaceRaised");
        lane.SetValue(HavenProperties.BorderColor, "Border");
        lane.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        lane.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        lane.Accessibility.AccessibleName = $"Board group {group.Title}";

        var header = new Container { Layout = HavenLayout.Horizontal };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        var title = new HavenText { Content = group.Title };
        title.SetValue(HavenProperties.FontSize, 15d);
        title.SetValue(HavenProperties.FontWeight, 700);
        title.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.Add(title);

        header.Add(ActionButton(
            "Left",
            $"Move group {group.Title} left",
            groupIndex > 0,
            new MoveGroupCommand(groupIndex, groupIndex - 1)));
        header.Add(ActionButton(
            "Right",
            $"Move group {group.Title} right",
            groupIndex < _snapshot.Groups.Count - 1,
            new MoveGroupCommand(groupIndex, groupIndex + 1)));

        var add = new HavenButton { Content = "Add card", Variant = ButtonVariant.Tertiary };
        add.Accessibility.AccessibleName = $"Add card to {group.Title}";
        add.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        EventHandler addHandler = (_, _) =>
            CommandRequested?.Invoke(
                this,
                new CreateCardCommand(group.Id, $"card-{Guid.NewGuid():N}", "New card"));
        add.Invoked += addHandler;
        _wiredButtons.Add(add);
        header.Add(add);
        lane.Add(header);

        if (group.Cards.Count == 0)
        {
            var empty = new HavenText { Content = "No cards yet" };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            empty.SetValue(HavenProperties.FontSize, 12d);
            lane.Add(empty);
            return lane;
        }

        for (var cardIndex = 0; cardIndex < group.Cards.Count; cardIndex++)
            lane.Add(BuildCard(group, groupIndex, group.Cards[cardIndex], cardIndex));

        return lane;
    }

    private Container BuildCard(HavenBoardGroup group, int groupIndex, HavenBoardCard card, int cardIndex)
    {
        var surface = new Container { Layout = HavenLayout.Vertical };
        surface.Name = $"BoardCard_{SafeName(card.Id)}";
        surface.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        surface.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(10)));
        surface.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        surface.SetValue(HavenProperties.Background, "Surface");
        surface.SetValue(HavenProperties.BorderColor, "Border");
        surface.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        surface.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        surface.Accessibility.AccessibleName = $"Board card {card.Title}";

        var title = new HavenText { Content = card.Title };
        title.SetValue(HavenProperties.FontSize, 13d);
        title.SetValue(HavenProperties.FontWeight, 600);
        surface.Add(title);

        var details = CardDetails(card);
        if (details.Length > 0)
        {
            var metadata = new HavenText { Content = details };
            metadata.SetValue(HavenProperties.Foreground, "TextSecondary");
            metadata.SetValue(HavenProperties.FontSize, 11d);
            surface.Add(metadata);
        }

        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        actions.SetValue(HavenProperties.Width, HavenLength.Percent(100));

        actions.Add(ActionButton(
            "Up",
            $"Move {card.Title} up",
            cardIndex > 0,
            new MoveCardCommand(group.Id, cardIndex, group.Id, cardIndex - 1)));
        actions.Add(ActionButton(
            "Down",
            $"Move {card.Title} down",
            cardIndex < group.Cards.Count - 1,
            new MoveCardCommand(group.Id, cardIndex, group.Id, cardIndex + 1)));

        if (groupIndex > 0)
        {
            var previous = _snapshot.Groups[groupIndex - 1];
            actions.Add(ActionButton(
                "Previous",
                $"Move {card.Title} to {previous.Title}",
                true,
                new MoveCardCommand(group.Id, cardIndex, previous.Id, previous.Cards.Count)));
        }
        else
        {
            actions.Add(ActionButton("Previous", $"Move {card.Title} to previous group", false, null));
        }

        if (groupIndex < _snapshot.Groups.Count - 1)
        {
            var next = _snapshot.Groups[groupIndex + 1];
            actions.Add(ActionButton(
                "Next",
                $"Move {card.Title} to {next.Title}",
                true,
                new MoveCardCommand(group.Id, cardIndex, next.Id, next.Cards.Count)));
        }
        else
        {
            actions.Add(ActionButton("Next", $"Move {card.Title} to next group", false, null));
        }

        surface.Add(actions);
        return surface;
    }

    private HavenButton ActionButton(string content, string accessibleName, bool enabled, HavenBoardCommand? command)
    {
        var button = new HavenButton { Content = content, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = accessibleName;
        button.SetValue(HavenProperties.Enabled, enabled);
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        if (command is not null)
        {
            EventHandler handler = (_, _) => CommandRequested?.Invoke(this, command);
            button.Invoked += handler;
        }
        _wiredButtons.Add(button);
        return button;
    }

    private void UnwireButtons()
    {
        // Buttons are discarded with the rebuilt subtree. Their event sources are instance-owned and become
        // collectible with the subtree; this list deliberately only releases our strong references.
        _wiredButtons.Clear();
    }

    private static string CardDetails(HavenBoardCard card)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(card.ParentCardId))
            details.Add("Nested card");
        var attachmentCount = card.Attachments?.Count ?? 0;
        if (attachmentCount > 0)
            details.Add(attachmentCount == 1 ? "1 attachment" : $"{attachmentCount} attachments");
        return string.Join(" · ", details);
    }

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static string SafeName(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "Item" : new string(chars);
    }

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="BoardsRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto Auto 1fr Auto" Gap="12px" Padding="22px" Background="Surface">
              <Text Name="BoardTitle" Row="0" Content="Haven Boards" Level="H1" />
              <Text Row="1" Content="Structured board · local first" Foreground="TextSecondary" FontSize="12" />
              <Container Name="BoardLanes" Row="2" Layout="Horizontal" Width="100%" Height="100%" Overflow="Scroll" Clip="true" Gap="12px" />
              <Text Name="Status" Row="3" Content="" Foreground="TextSecondary" FontSize="11" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "Boards.hui");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireButtons();
        CommandRequested = null;
    }
}

using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardsHuiSceneTests
{
    [Fact]
    public void SetSnapshot_builds_accessible_lanes_cards_and_disabled_states()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        Assert.Equal("Haven Boards", scene.BoardTitle.Content);
        Assert.Equal(3, scene.BoardLanes.Children.OfType<Container>().Count());

        var firstLane = Assert.IsType<Container>(scene.BoardLanes.Children[0]);
        Assert.Equal("Board group To do", firstLane.Accessibility.AccessibleName);

        var firstCard = scene.Root.DescendantsAndSelf()
            .Single(element => element.Name == "BoardCard_card1");
        Assert.Equal("Board card First task", firstCard.Accessibility.AccessibleName);

        var moveUp = FindButton(scene, "Move First task up");
        Assert.False(moveUp.GetValue(HavenProperties.Enabled));
        Assert.False(moveUp.Accessibility.Enabled);
        Assert.True(moveUp.State.HasFlag(HavenElementState.Disabled));
    }

    [Fact]
    public void Enabled_keyboard_move_emits_typed_neutral_command()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveNext = FindButton(scene, "Move First task to Doing");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);

        Assert.True(moveNext.KeyDown(enter));
        Assert.True(moveNext.KeyUp(enter));

        var command = Assert.IsType<MoveCardCommand>(requested);
        Assert.Equal("todo", command.FromGroupId);
        Assert.Equal(0, command.FromIndex);
        Assert.Equal("doing", command.ToGroupId);
        Assert.Equal(1, command.ToIndex);
    }

    [Fact]
    public void Disabled_keyboard_move_cannot_emit_command()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveUp = FindButton(scene, "Move First task up");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);

        Assert.True(moveUp.KeyDown(enter));
        Assert.True(moveUp.KeyUp(enter));
        Assert.Null(requested);
    }

    [Fact]
    public void Status_visibility_tracks_empty_and_non_empty_messages()
    {
        using var scene = new HavenBoardsHuiScene();

        scene.SetStatus("Saved locally");
        Assert.Equal("Saved locally", scene.Status.Content);
        Assert.Equal(HavenVisibility.Visible, scene.Status.GetValue(HavenProperties.Visibility));

        scene.SetStatus(null);
        Assert.Equal(string.Empty, scene.Status.Content);
        Assert.Equal(HavenVisibility.Collapsed, scene.Status.GetValue(HavenProperties.Visibility));
    }

    private static HavenButton FindButton(HavenBoardsHuiScene scene, string accessibleName) =>
        scene.Root.DescendantsAndSelf()
            .OfType<HavenButton>()
            .Single(button => button.Accessibility.AccessibleName == accessibleName);
}

using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;

namespace CakeOS.HuiLinuxHost.Canvas;

internal sealed class CanvasToolStrip : Container
{
    public CanvasToolStrip()
    {
        Name = "Canvas.Tools";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Px(42));
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        PenButton = CreateButton("Canvas.Tool.Pen", "Pen", 112);
        EraserButton = CreateButton("Canvas.Tool.Eraser", "Eraser", 128);
        UndoButton = CreateButton("Canvas.Undo", "Undo", 112);
        RedoButton = CreateButton("Canvas.Redo", "Redo", 112);

        PenButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasStrokeTool.Pen);
        EraserButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasStrokeTool.Eraser);
        UndoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        RedoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);

        Add(PenButton);
        Add(EraserButton);
        Add(UndoButton);
        Add(RedoButton);

        SetTool(CanvasStrokeTool.Pen);
        SetHistory(canUndo: false, canRedo: false);
    }

    public HuiButton PenButton { get; }
    public HuiButton EraserButton { get; }
    public HuiButton UndoButton { get; }
    public HuiButton RedoButton { get; }

    public event Action<CanvasStrokeTool>? ToolRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;

    public void SetTool(CanvasStrokeTool tool)
    {
        SetSelected(PenButton, tool == CanvasStrokeTool.Pen);
        SetSelected(EraserButton, tool == CanvasStrokeTool.Eraser);
    }

    public void SetHistory(bool canUndo, bool canRedo)
    {
        UndoButton.SetValue(HavenProperties.Enabled, canUndo);
        RedoButton.SetValue(HavenProperties.Enabled, canRedo);
        UndoButton.SetState(HavenElementState.Disabled, !canUndo);
        RedoButton.SetState(HavenElementState.Disabled, !canRedo);
    }

    private static HuiButton CreateButton(string name, string content, double width)
    {
        var button = new HuiButton { Name = name, Content = content };
        button.SetValue(HavenProperties.Width, HavenLength.Px(width));
        button.SetValue(HavenProperties.Height, HavenLength.Px(36));
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static void SetSelected(HuiButton button, bool selected)
    {
        button.SetState(HavenElementState.Selected, selected);
        button.Accessibility.Selected = selected;
    }
}

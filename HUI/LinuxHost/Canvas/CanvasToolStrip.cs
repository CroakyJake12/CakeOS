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

        PenButton = CreateButton("Canvas.Tool.Pen", "Pen", 82);
        HighlighterButton = CreateButton("Canvas.Tool.Highlighter", "Highlight", 88);
        EraserButton = CreateButton("Canvas.Tool.Eraser", "Eraser", 78);
        SelectorButton = CreateButton("Canvas.Tool.Selector", "Select", 78);
        ShapeButton = CreateButton("Canvas.Tool.Shape", "Shape", 78);
        UndoButton = CreateButton("Canvas.Undo", "Undo", 82);
        RedoButton = CreateButton("Canvas.Redo", "Redo", 82);

        PenButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Pen);
        HighlighterButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Highlighter);
        EraserButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Eraser);
        SelectorButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Selector);
        ShapeButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Shape);
        UndoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        RedoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);

        Add(PenButton);
        Add(HighlighterButton);
        Add(EraserButton);
        Add(SelectorButton);
        Add(ShapeButton);
        Add(UndoButton);
        Add(RedoButton);

        SetTool(CanvasTool.Pen);
        SetHistory(canUndo: false, canRedo: false);
    }

    public HuiButton PenButton { get; }
    public HuiButton HighlighterButton { get; }
    public HuiButton EraserButton { get; }
    public HuiButton SelectorButton { get; }
    public HuiButton ShapeButton { get; }
    public HuiButton UndoButton { get; }
    public HuiButton RedoButton { get; }

    public event Action<CanvasTool>? ToolRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;

    public void SetTool(CanvasTool tool)
    {
        SetSelected(PenButton, tool == CanvasTool.Pen);
        SetSelected(HighlighterButton, tool == CanvasTool.Highlighter);
        SetSelected(EraserButton, tool == CanvasTool.Eraser);
        SetSelected(SelectorButton, tool == CanvasTool.Selector);
        SetSelected(ShapeButton, tool == CanvasTool.Shape);
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
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 10px"));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static void SetSelected(HuiButton button, bool selected)
    {
        button.SetState(HavenElementState.Selected, selected);
        button.Accessibility.Selected = selected;
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CanvasPath = Avalonia.Controls.Shapes.Path;

namespace CakeOS.Canvas.App;

/// <summary>
/// Minimal ICommand without extra dependencies.
/// </summary>
internal sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : System.Windows.Input.ICommand
{
    private readonly Action _execute = execute;
    private readonly Func<bool>? _canExecute = canExecute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The real Canvas application shell, shared by every host. Native Avalonia
/// chrome (menu, compact icon toolbar, tool options, status bar) around the
/// Rnote-backed <see cref="CanvasView"/>. No demo text, no proof copy in the
/// normal interface; diagnostics stay behind Console markers and the
/// self-test path.
/// </summary>
public sealed class CanvasShell : UserControl, IDisposable
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#8a7cff"));
    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.Parse("#37314f"));
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.Parse("#262832"));
    private static readonly IBrush DefaultBackground = Brushes.Transparent;
    private static readonly IBrush MutedForeground = new SolidColorBrush(Color.Parse("#a8afbd"));
    private static readonly IBrush StrongForeground = new SolidColorBrush(Color.Parse("#f5f7fb"));

    private readonly CanvasController _controller;
    private readonly CanvasView _view;
    private readonly string _documentsDir;
    private readonly Dictionary<CanvasTool, ToggleButton> _toolButtons = new();
    private readonly Dictionary<CanvasShape, ToggleButton> _shapeButtons = new();
    private readonly Dictionary<string, Button> _swatches = new();
    private Slider _widthSlider = null!;
    private TextBlock _widthValue = null!;
    private StackPanel _shapeRow = null!;
    private ToggleButton _eraserSplitButton = null!;
    private StackPanel _optionsRow = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private TextBlock _statusText = null!;
    private TextBlock _docInfo = null!;
    private TextBlock _zoomLabel = null!;
    private Slider _zoomSlider = null!;
    private readonly DelegateCommand _undoCommand;
    private readonly DelegateCommand _redoCommand;
    private bool _updating;
    private bool _disposed;

    public CanvasShell(CanvasController controller, string documentsDir)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDir);
        _documentsDir = documentsDir;

        var root = new DockPanel { LastChildFill = true };

        // Commands exist before menu construction so menu items can bind them.
        _undoCommand = new DelegateCommand(Undo, () => _controller.CanUndo);
        _redoCommand = new DelegateCommand(Redo, () => _controller.CanRedo);

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        _optionsRow = BuildOptionsRow();
        DockPanel.SetDock(_optionsRow, Dock.Top);
        root.Children.Add(_optionsRow);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        _view = new CanvasView(_controller);
        root.Children.Add(_view);

        Content = root;

        _controller.StateChanged += Refresh;
        _view.ViewChanged += RefreshHistory;

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new DelegateCommand(() => _ = NewAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.O, KeyModifiers.Control), Command = new DelegateCommand(() => _ = OpenAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, KeyModifiers.Control), Command = new DelegateCommand(() => _ = SaveAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control), Command = _undoCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Y, KeyModifiers.Control), Command = _redoCommand });

        Refresh();
        _view.RefreshFrame();

        if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_OPEN_AT_START") == "1")
            OpenAtStart();
    }

    public event EventHandler? TitleChanged;

    public string WindowTitle => _controller.WindowTitle;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.StateChanged -= Refresh;
        _view.ViewChanged -= RefreshHistory;
        _view.Dispose();
        _controller.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Input self-test driving the exact toolbar handlers.</summary>
    public void RunInputSelfTest()
    {
        OnToolSelected(CanvasTool.Eraser);
        if (_controller.Tool != CanvasTool.Eraser)
            throw new InvalidOperationException("Toolbar did not select the eraser tool.");
        OnToolSelected(CanvasTool.Pen);
        if (_controller.Tool != CanvasTool.Pen)
            throw new InvalidOperationException("Toolbar did not restore the pen tool.");

        _controller.Session.BeginStroke(200, 200, 0.5);
        _controller.Session.EndStroke(260, 240, 0.5);
        _view.RefreshFrame();
        if (!_controller.CanUndo)
            throw new InvalidOperationException("Self-test expected an undoable stroke.");
        Undo();
        if (!_controller.CanRedo)
            throw new InvalidOperationException("Self-test undo did not expose redo history.");
        Redo();
        if (!_controller.CanUndo)
            throw new InvalidOperationException("Self-test redo did not restore undo history.");

        Console.WriteLine("CANVAS_RNOTE_HUI_TOOLBAR_READY pen=1 eraser=1 undo=1 redo=1");
        _controller.SetStatus("Ready");
    }

    /// <summary>Automation hook: save-on-close uses the exact Save path.</summary>
    public void SaveOnClose()
    {
        if (!_controller.IsDirty)
            return;
        try
        {
            if (!_controller.Save())
                _controller.SaveTo(DefaultDocumentPath());
        }
        catch
        {
            // Status already records the truthful failure; close proceeds.
        }
    }

    public async Task<bool> TryCloseAsync()
    {
        if (!_controller.IsDirty)
            return true;
        if (_controller.DocumentPath is not null)
        {
            try
            {
                _controller.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }
        var path = await PickSavePathAsync().ConfigureAwait(true);
        if (path is null)
            return false;
        try
        {
            _controller.SaveTo(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string DefaultDocumentPath() =>
        Path.Combine(_documentsDir, "canvas.rnote");

    private void OpenAtStart()
    {
        var path = DefaultDocumentPath();
        try
        {
            if (!File.Exists(path))
            {
                _controller.SetStatus("No saved document yet");
                Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_EMPTY path={path}");
                return;
            }
            _controller.OpenDocument(path, File.ReadAllBytes(path));
            _view.ResetDocument();
        }
        catch (Exception exception)
        {
            _controller.SetStatus($"Reopen failed: {exception.Message}");
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_FAILED path={path} reason={exception.Message}");
        }
    }

    private void Undo()
    {
        _controller.Undo();
        _view.RefreshFrame();
    }

    private void Redo()
    {
        _controller.Redo();
        _view.RefreshFrame();
    }

    private void OnToolSelected(CanvasTool tool)
    {
        if (tool == CanvasTool.Eraser || tool == CanvasTool.Selector)
        {
            // Selection-affecting tools apply immediately; viewport state is untouched.
        }
        _controller.SelectTool(tool);
        _view.RefreshFrame();
    }

    private void OnShapeSelected(CanvasShape shape)
    {
        _controller.SelectShape(shape);
        if (_controller.Tool != CanvasTool.Shape)
            OnToolSelected(CanvasTool.Shape);
    }

    private void OnColorSelected(CanvasRgba color)
    {
        _controller.SetColor(color);
        _view.RefreshFrame();
    }

    private async Task NewAsync()
    {
        if (_controller.IsDirty)
        {
            if (_controller.DocumentPath is not null)
            {
                try { _controller.Save(); }
                catch { return; }
            }
            else
            {
                var path = await PickSavePathAsync().ConfigureAwait(true);
                if (path is null)
                    return;
                try { _controller.SaveTo(path); }
                catch { return; }
            }
        }
        _controller.NewDocument();
        _view.ResetDocument();
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OpenAsync()
    {
        var file = await PickOpenFileAsync().ConfigureAwait(true);
        if (file is null)
            return;
        try
        {
            await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(true);
            var local = file.TryGetLocalPath();
            _controller.OpenDocument(local ?? file.Name, memory.ToArray());
            _view.ResetDocument();
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _controller.SetStatus($"Open failed: {exception.Message}");
        }
    }

    private async Task SaveAsync()
    {
        if (_controller.DocumentPath is not null)
        {
            try { _controller.Save(); }
            catch { /* status already truthful */ }
            return;
        }
        var path = await PickSavePathAsync().ConfigureAwait(true);
        if (path is null)
            return;
        try { _controller.SaveTo(path); }
        catch { /* status already truthful */ }
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IStorageFile?> PickOpenFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
            return null;
        var options = new FilePickerOpenOptions
        {
            Title = "Open Canvas document",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Rnote document") { Patterns = ["*.rnote"] }],
        };
        try
        {
            var folder = await provider.TryGetFolderFromPathAsync(_documentsDir).ConfigureAwait(true);
            if (folder is not null)
                options.SuggestedStartLocation = folder;
        }
        catch { /* best effort */ }
        var files = await provider.OpenFilePickerAsync(options).ConfigureAwait(true);
        return files.Count == 0 ? null : files[0];
    }

    private async Task<string?> PickSavePathAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
            return null;
        var options = new FilePickerSaveOptions
        {
            Title = "Save Canvas document",
            SuggestedFileName = _controller.DocumentName,
            DefaultExtension = "rnote",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("Rnote document") { Patterns = ["*.rnote"] }],
        };
        try
        {
            var folder = await provider.TryGetFolderFromPathAsync(_documentsDir).ConfigureAwait(true);
            if (folder is not null)
                options.SuggestedStartLocation = folder;
        }
        catch { /* best effort */ }
        var file = await provider.SaveFilePickerAsync(options).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    private void Refresh()
    {
        if (_disposed)
            return;
        _updating = true;
        try
        {
            foreach (var (tool, button) in _toolButtons)
            {
                button.IsChecked = _controller.Tool == tool;
                StyleToolButton(button, _controller.Tool == tool);
            }
            foreach (var (shape, button) in _shapeButtons)
            {
                button.IsChecked = _controller.Shape == shape;
                StyleToolButton(button, _controller.Shape == shape);
            }
            _eraserSplitButton.IsChecked = _controller.EraserStyle == CanvasEraserStyle.Split;
            StyleToolButton(_eraserSplitButton, _controller.EraserStyle == CanvasEraserStyle.Split);
            _widthSlider.Value = _controller.CurrentWidth;
            _widthValue.Text = $"{_controller.CurrentWidth:0.#}";
            _shapeRow.IsVisible = _controller.Tool == CanvasTool.Shape;
            _eraserSplitButton.IsVisible = _controller.Tool == CanvasTool.Eraser;
            _optionsRow.IsVisible = _controller.Tool is CanvasTool.Pen or CanvasTool.Highlighter or CanvasTool.Shape or CanvasTool.Eraser;
            RefreshHistory();
            _statusText.Text = _controller.StatusText;
            _docInfo.Text = _controller.IsDirty ? $"{_controller.DocumentName} •" : _controller.DocumentName;
            _zoomLabel.Text = $"{_controller.Zoom:0.#}x";
            if (Math.Abs(_zoomSlider.Value - _controller.Zoom) > 0.001d)
                _zoomSlider.Value = _controller.Zoom;
        }
        finally
        {
            _updating = false;
        }
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshHistory()
    {
        _undoButton.IsEnabled = _controller.CanUndo;
        _redoButton.IsEnabled = _controller.CanRedo;
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
    }

    private Menu BuildMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(MenuEntry("New", "Ctrl+N", () => _ = NewAsync()));
        file.Items.Add(MenuEntry("Open…", "Ctrl+O", () => _ = OpenAsync()));
        file.Items.Add(new Separator());
        file.Items.Add(MenuEntry("Save", "Ctrl+S", () => _ = SaveAsync()));
        file.Items.Add(MenuEntry("Save As…", null, () => _ = SaveAsAsync()));
        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(MenuEntry("Undo", "Ctrl+Z", Undo, _undoCommand));
        edit.Items.Add(MenuEntry("Redo", "Ctrl+Y", Redo, _redoCommand));
        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        return menu;
    }

    private static MenuItem MenuEntry(string header, string? gesture, Action run, System.Windows.Input.ICommand? command = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture is not null)
            item.InputGesture = KeyGesture.Parse(gesture);
        if (command is not null)
            item.Command = command;
        else
            item.Click += (_, _) => run();
        return item;
    }

    private async Task SaveAsAsync()
    {
        var path = await PickSavePathAsync().ConfigureAwait(true);
        if (path is null)
            return;
        try { _controller.SaveTo(path); }
        catch { /* status already truthful */ }
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private WrapPanel BuildToolbar()
    {
        var bar = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        bar.Margin = new Thickness(8, 6, 8, 2);
        AddTool(bar, CanvasTool.Pen, CanvasIcons.Pen, "Pen");
        AddTool(bar, CanvasTool.Highlighter, CanvasIcons.Highlighter, "Highlighter");
        AddTool(bar, CanvasTool.Eraser, CanvasIcons.Eraser, "Eraser");
        AddTool(bar, CanvasTool.Selector, CanvasIcons.Select, "Select");
        AddTool(bar, CanvasTool.Shape, CanvasIcons.Shape, "Shape");
        bar.Children.Add(Divider());
        foreach (var preset in CanvasController.Palette)
            bar.Children.Add(Swatch(preset));
        bar.Children.Add(Divider());
        _undoButton = ChromeButton(CanvasIcons.Undo, "Undo", Undo);
        _redoButton = ChromeButton(CanvasIcons.Redo, "Redo", Redo);
        bar.Children.Add(_undoButton);
        bar.Children.Add(_redoButton);
        return bar;
    }

    private StackPanel BuildOptionsRow()
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Margin = new Thickness(12, 2, 12, 6);
        _shapeRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        AddShapeChip(_shapeRow, CanvasShape.Rectangle, CanvasIcons.ShapeRect, "Rectangle");
        AddShapeChip(_shapeRow, CanvasShape.Ellipse, CanvasIcons.ShapeEllipse, "Ellipse");
        AddShapeChip(_shapeRow, CanvasShape.Line, CanvasIcons.ShapeLine, "Line");
        AddShapeChip(_shapeRow, CanvasShape.Arrow, CanvasIcons.ShapeArrow, "Arrow");
        row.Children.Add(_shapeRow);
        _eraserSplitButton = new ToggleButton { Content = new TextBlock { Text = "Split", FontSize = 12 }, Padding = new Thickness(10, 4, 10, 4) };
        ToolTip.SetTip(_eraserSplitButton, "Split colliding strokes instead of erasing them");
        AutomationProperties.SetName(_eraserSplitButton, "Split colliding strokes");
        _eraserSplitButton.Click += (_, _) => _controller.SetEraserStyle(
            _controller.EraserStyle == CanvasEraserStyle.Split ? CanvasEraserStyle.Trash : CanvasEraserStyle.Split);
        row.Children.Add(_eraserSplitButton);
        var widthLabel = new TextBlock { Text = "Width", FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        widthLabel.Foreground = MutedForeground;
        row.Children.Add(widthLabel);
        var slider = new Slider { Minimum = CanvasController.MinToolWidth, Maximum = CanvasController.MaxToolWidth, Width = 140, TickFrequency = 1 };
        slider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _controller.SetWidth(slider.Value);
        };
        row.Children.Add(slider);
        _widthSlider = slider;
        _widthValue = new TextBlock { FontSize = 12, Width = 36, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _widthValue.Foreground = MutedForeground;
        row.Children.Add(_widthValue);
        return row;
    }

    private Border BuildStatusBar()
    {
        var border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#262832")),
            Padding = new Thickness(12, 4, 12, 4),
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var left = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        _docInfo = new TextBlock { FontSize = 12 };
        _docInfo.Foreground = StrongForeground;
        _statusText = new TextBlock { FontSize = 12 };
        _statusText.Foreground = MutedForeground;
        left.Children.Add(_docInfo);
        left.Children.Add(_statusText);
        var right = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        var zoomOut = ChromeButton(CanvasIcons.ZoomOut, "Zoom out", () => _controller.SetZoom(_controller.Zoom / 1.25));
        _zoomLabel = new TextBlock { FontSize = 12, Width = 52, TextAlignment = TextAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _zoomLabel.Foreground = MutedForeground;
        _zoomSlider = new Slider { Minimum = CanvasController.MinZoom, Maximum = CanvasController.MaxZoom, Width = 120, TickFrequency = 1 };
        var slider = _zoomSlider;
        slider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _controller.SetZoom(slider.Value);
        };
        var zoomIn = ChromeButton(CanvasIcons.ZoomIn, "Zoom in", () => _controller.SetZoom(_controller.Zoom * 1.25));
        var fit = ChromeButton(CanvasIcons.Fit, "Fit to content", () => _view.FitToContent());
        right.Children.Add(zoomOut);
        right.Children.Add(_zoomLabel);
        right.Children.Add(_zoomSlider);
        right.Children.Add(zoomIn);
        right.Children.Add(fit);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        border.Child = row;
        return border;
    }

    private void AddTool(WrapPanel bar, CanvasTool tool, string icon, string name)
    {
        var button = new ToggleButton
        {
            Content = Icon(icon),
            Width = 34,
            Height = 32,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(0),
        };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => OnToolSelected(tool);
        _toolButtons[tool] = button;
        bar.Children.Add(button);
    }

    private void AddShapeChip(StackPanel row, CanvasShape shape, string icon, string name)
    {
        var button = new ToggleButton
        {
            Content = Icon(icon),
            Width = 32,
            Height = 28,
            Padding = new Thickness(0),
        };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => OnShapeSelected(shape);
        _shapeButtons[shape] = button;
        row.Children.Add(button);
    }

    private Button Swatch(CanvasRgba color)
    {
        var key = $"{color.R:0.###},{color.G:0.###},{color.B:0.###}";
        var dot = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(255,
                (byte)Math.Clamp(color.R * 255, 0, 255),
                (byte)Math.Clamp(color.G * 255, 0, 255),
                (byte)Math.Clamp(color.B * 255, 0, 255))),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#3a3d47")),
        };
        var button = new Button
        {
            Content = dot,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 2, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, $"Colour {key}");
        AutomationProperties.SetName(button, $"Colour {key}");
        button.Click += (_, _) => OnColorSelected(color);
        _swatches[key] = button;
        return button;
    }

    private static Button ChromeButton(string icon, string name, Action run)
    {
        var button = new Button
        {
            Content = Icon(icon),
            Width = 34,
            Height = 32,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(0),
        };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => run();
        return button;
    }

    private static CanvasPath Icon(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = StrongForeground,
        StrokeThickness = 1.7,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = 18,
        Height = 18,
        Stretch = Stretch.Uniform,
    };

    private static Control Divider() => new Border
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(4, 4, 8, 8),
        Background = new SolidColorBrush(Color.Parse("#2c2f3a")),
    };

    private static void StyleToolButton(ToggleButton button, bool selected)
    {
        button.Background = selected ? SelectedBackground : DefaultBackground;
        button.BorderBrush = selected ? AccentBrush : Brushes.Transparent;
        button.BorderThickness = new Thickness(1);
    }
}

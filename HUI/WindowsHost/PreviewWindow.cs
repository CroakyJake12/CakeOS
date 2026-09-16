using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Canvas.App;
using CakeOS.Platform;
using Haven.UI.Components;
using HuiPage = Haven.UI.Components.Page;

namespace CakeOS.HuiWindowsHost;

public sealed class PreviewWindow : Window
{
    private readonly HuiPreviewSurface? _surface;
    private readonly CanvasShell? _canvasShell;

    public PreviewWindow() : this((IRootElement?)null)
    {
    }

    public PreviewWindow(IRootElement? root)
    {
        if (root is not null)
            throw new NotSupportedException("The Windows HUI host cannot render an unadapted platform root.");

        _surface = new HuiPreviewSurface();

        Title = "CakeOS HUI Windows Preview";
        Width = 960;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Content = _surface;
        Closed += (_, _) => _surface.Dispose();
    }

    /// <summary>Hosts the real Canvas application shell.</summary>
    public PreviewWindow(CanvasShell shell)
    {
        _canvasShell = shell ?? throw new ArgumentNullException(nameof(shell));
        Title = shell.WindowTitle;
        shell.TitleChanged += (_, _) => Title = shell.WindowTitle;
        Width = 1100;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        Background = new SolidColorBrush(Color.Parse("#14161c"));
        Content = shell;
        Closing += async (_, e) =>
        {
            if (!await shell.TryCloseAsync().ConfigureAwait(true))
                e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_SAVE_ON_CLOSE") == "1")
                shell.SaveOnClose();
            shell.Dispose();
        };
    }

    /// <summary>Mounts a caller-owned HUI page (e.g. a Boards session scene).</summary>
    public PreviewWindow(HuiPage page, string title)
    {
        ArgumentNullException.ThrowIfNull(page);
        _surface = new HuiPreviewSurface(page);
        Title = title;
        Width = 1100;
        Height = 700;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Content = _surface;
        Closed += (_, _) => _surface.Dispose();
    }

    public void RunInputSelfTest()
    {
        if (_canvasShell is not null)
        {
            _canvasShell.RunInputSelfTest();
            Title = $"{_canvasShell.WindowTitle} — input passed";
            return;
        }
        _surface!.RunInputSelfTest();
        Title = "CakeOS HUI Windows Preview - input passed";
    }
}

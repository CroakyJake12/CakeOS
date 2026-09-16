using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Canvas.App;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost;

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
            throw new NotSupportedException("The Linux HUI host cannot render an unadapted platform root.");

        _surface = new HuiPreviewSurface();

        Title = "CakeOS HUI Linux Preview";
        Width = 960;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Content = _surface;
        Closed += (_, _) => _surface.Dispose();
    }

    /// <summary>Hosts the real Canvas application shell (shared with Windows).</summary>
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

    public void RunInputSelfTest()
    {
        if (_canvasShell is not null)
        {
            _canvasShell.RunInputSelfTest();
            Title = $"{_canvasShell.WindowTitle} — input passed";
            return;
        }
        _surface!.RunInputSelfTest();
        Title = "CakeOS HUI Linux Preview - input passed";
    }
}

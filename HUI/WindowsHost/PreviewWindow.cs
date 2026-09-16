using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Platform;
using Haven.UI.Components;

namespace CakeOS.HuiWindowsHost;

public sealed class PreviewWindow : Window
{
    private readonly bool _canvasMode = Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1";
    private readonly HuiPreviewSurface _surface;

    public PreviewWindow() : this((IRootElement?)null)
    {
    }

    public PreviewWindow(IRootElement? root)
    {
        if (root is not null)
            throw new NotSupportedException("The Windows HUI host cannot render an unadapted platform root.");

        _surface = new HuiPreviewSurface();

        Title = _canvasMode ? "CakeOS HUI Canvas / Rnote Preview (Windows)" : "CakeOS HUI Windows Preview";
        Width = 960;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Content = _surface;
        Closed += (_, _) => _surface.Dispose();
    }

    public void RunInputSelfTest()
    {
        _surface.RunInputSelfTest();
        Title = _canvasMode
            ? "CakeOS HUI Canvas / Rnote Preview (Windows) - input passed"
            : "CakeOS HUI Windows Preview - input passed";
    }
}

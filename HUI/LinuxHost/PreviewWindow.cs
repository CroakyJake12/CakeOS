using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.HuiLinuxHost;

public sealed class PreviewWindow : Window
{
    private readonly bool _canvasMode = Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1";
    private readonly HuiPreviewSurface _surface;

    public PreviewWindow() : this(new HuiPreviewSurface())
    {
    }

    public PreviewWindow(HuiPreviewSurface surface)
    {
        _surface = surface;
        Title = _canvasMode ? "CakeOS HUI Canvas / Rnote Preview" : "CakeOS HUI Linux Preview";
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
            ? "CakeOS HUI Canvas / Rnote Preview - input passed"
            : "CakeOS HUI Linux Preview - input passed";
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.HuiLinuxHost;

public sealed class PreviewWindow : Window
{
    private readonly HuiPreviewSurface _surface = new();

    public PreviewWindow()
    {
        Title = "CakeOS HUI Linux Preview";
        Width = 960;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Content = _surface;
    }

    public void RunInputSelfTest()
    {
        _surface.RunInputSelfTest();
        Title = "CakeOS HUI Linux Preview - input passed";
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.HuiLinuxHost.Canvas;

namespace CakeOS.HuiLinuxHost;

public sealed class App : Application
{
    internal static IHuiRootProvider? RootProvider { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1")
                CanvasManagedBoundaryProof.Run();

            var root = RootProvider is null
                ? null
                : RootProvider.CreateRoot() ?? throw new InvalidOperationException("HUI root provider returned null.");
            var window = root is null
                ? new PreviewWindow()
                : new PreviewWindow(new HuiPreviewSurface(root));
            desktop.MainWindow = window;

            window.Opened += (_, _) =>
            {
                if (Environment.GetEnvironmentVariable("CAKEOS_HUI_PREVIEW_SELF_TEST") == "1")
                    Dispatcher.UIThread.Post(window.RunInputSelfTest, DispatcherPriority.Background);

                if (int.TryParse(Environment.GetEnvironmentVariable("CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS"), out var ms) && ms > 0)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                    timer.Start();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

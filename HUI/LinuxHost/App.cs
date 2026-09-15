using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.HuiLinuxHost.Canvas;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace CakeOS.HuiLinuxHost;

public sealed class App : Application
{
    internal static IHuiRootProvider? RootProvider { get; set; }
    internal static IServiceProvider? Services { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1")
                CanvasManagedBoundaryProof.Run();

            var root = RootProvider is null
                ? null
                : RootProvider.CreateRoot(Services) ?? throw new InvalidOperationException("HUI root provider returned null.");
            var window = new PreviewWindow(root);
            desktop.MainWindow = window;

            window.Opened += async (_, _) =>
            {
                if (RootProvider is not null)
                {
                    var initState = await RootProvider.InitializeAsync(Services).ConfigureAwait(false);
                    if (initState != HuiRootLifecycleState.Active)
                    {
                        await RootProvider.ActivateAsync().ConfigureAwait(false);
                    }
                }

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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IVersionedSettingsStore>(_ =>
        {
            var layout = new XdgPlatformStorageLayout(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "haven"));
            return new VersionedSettingsStore(layout);
        });
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IModelGovernance, ModelGovernance>();
        services.AddSingleton<IProductRegistry, ProductRegistry>();
        services.AddSingleton<IProductRouter, RegistryBackedRouter>(sp =>
            new RegistryBackedRouter(sp.GetRequiredService<IProductRegistry>()));
    }
}
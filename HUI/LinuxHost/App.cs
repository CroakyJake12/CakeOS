using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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

            // Adapter test window
            var window = new Window
            {
                Title = "CakeOS Linux Adapters",
                Width = 800,
                Height = 600
            };
            desktop.MainWindow = window;
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
        
        // Linux adapters
        services.AddSingleton<ISecretStore, Adapters.LibsecretSecretStore>();
        services.AddSingleton<IFilePicker, Adapters.PortalFilePicker>();
        services.AddSingleton<IAudioManager, Adapters.PipeWireAudioManager>();
        services.AddSingleton<IScreenShare, Adapters.PortalScreenShare>();
        services.AddSingleton<IGlobalShortcuts, Adapters.PortalGlobalShortcuts>();
        services.AddSingleton<IOverlayManager, Adapters.PortalOverlayManager>();
        services.AddSingleton<INotificationTransport, Adapters.FreedesktopNotificationTransport>();
        services.AddSingleton<IScheduler, Adapters.SystemdScheduler>();
    }
}
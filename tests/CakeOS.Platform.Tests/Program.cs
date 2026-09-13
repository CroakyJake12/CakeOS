using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

var suite = new PlatformFoundationSuite();
await suite.RunAsync();

internal sealed class PlatformFoundationSuite
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-platform-tests", Guid.NewGuid().ToString("N"), "haven");

    public async Task RunAsync()
    {
        try
        {
            await VerifyRegistryAndRoutingAsync();
            VerifyHuiHostAbi();
            await VerifyPersistenceAndPermissionsAsync();
            await VerifyProviderRegistryAndGovernanceAsync();
            await VerifyNotificationEventsAsync();
            Console.WriteLine("CakeOS shared platform foundation tests passed.");
        }
        finally
        {
            if (Directory.Exists(_dataRoot))
                Directory.Delete(Path.GetDirectoryName(_dataRoot)!, recursive: true);
        }
    }

    private async Task VerifyRegistryAndRoutingAsync()
    {
        var registry = new ProductRegistry();
        Equal("cakeos.shared-app-registry", registry.Identity.RegistryId, "registry id");
        Equal(1, registry.Identity.RegistrySchemaVersion, "registry schema version");

        var registration = new ProductRegistration(
            ProductId: "test.application",
            DisplayName: "Test Application",
            ProductType: ProductType.App,
            Route: "/test",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "TestEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create, AppLifecycleOperation.Activate, AppLifecycleOperation.RequestClose],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(registration);
        
        var router = new RegistryBackedRouter(registry);
        var services = new ServiceCollection().BuildServiceProvider();
        var permissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot)), () => DateTimeOffset.UnixEpoch);
        
        // Need to grant permission first
        await permissions.GrantAsync("product.test.application", "launch", "execute", GrantSource.System);
        
        var resolved = await router.ResolveAsync(new ProductRouteRequest(registry.Identity, registration.ProductId, services), permissions);
        Equal(RouteResolutionKind.Resolved, resolved.Kind, "registered route resolved");
        NotNull(resolved.Root, "root created");
        Equal(registration.ProductId, resolved.Registration?.ProductId, "registration returned");
        
        Equal(RouteResolutionKind.UnknownProduct, (await router.ResolveAsync(new ProductRouteRequest(registry.Identity, "missing.application", services), permissions)).Kind, "unknown route");
        Equal(
            RouteResolutionKind.IncompatibleRegistry,
            (await router.ResolveAsync(new ProductRouteRequest(new RegistryIdentity("other.registry", 1), registration.ProductId, services), permissions)).Kind,
            "mismatched registry");
        
        // Test capability denial
        var noGrantPermissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(Path.Combine(Path.GetTempPath(), "cakeos-platform-tests", Guid.NewGuid().ToString("N"), "haven"))), () => DateTimeOffset.UnixEpoch);
        var denied = await router.ResolveAsync(new ProductRouteRequest(registry.Identity, registration.ProductId, services), noGrantPermissions);
        Equal(RouteResolutionKind.CapabilityDenied, denied.Kind, "capability denied without grant");
    }

    private static void VerifyHuiHostAbi()
    {
        HuiLinuxHostAbi.RequireCompatible(HuiLinuxHostAbi.Current);
        Throws<NotSupportedException>(
            () => HuiLinuxHostAbi.RequireCompatible(new HuiRootProviderAbi(HuiLinuxHostAbi.ContractId, HuiLinuxHostAbi.CurrentVersion + 1)),
            "future HUI ABI must be rejected");
    }

    private async Task VerifyPersistenceAndPermissionsAsync()
    {
        var xdgDataHome = Path.Combine(_dataRoot, "xdg-data-home");
        var xdgLayout = XdgPlatformStorageLayout.FromEnvironment(name => name == "XDG_DATA_HOME" ? xdgDataHome : null);
        Equal(Path.Combine(xdgDataHome, "haven"), xdgLayout.DataRoot, "XDG data root");
        Equal(Path.Combine(xdgLayout.DataRoot, "platform", "settings.v1.json"), xdgLayout.SettingsFile, "shared settings path");
        Equal(Path.Combine(xdgLayout.DataRoot, "config"), xdgLayout.ConfigDirectory, "config directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "cache"), xdgLayout.CacheDirectory, "cache directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "state"), xdgLayout.StateDirectory, "state directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "models"), xdgLayout.ModelsDirectory, "models directory");

        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        await settings.SetAsync("test.setting", new TestSetting("persisted"));
        var reopened = new VersionedSettingsStore(layout);
        Equal("persisted", (await reopened.GetAsync<TestSetting>("test.setting"))?.Value, "versioned settings persistence");

        // Test migrations
        var migrationResult = await settings.MigrateAsync(1, async (entries, fromVersion) =>
        {
            entries["migrated"] = JsonSerializer.SerializeToElement(true);
            return entries;
        });
        True(migrationResult.Success, "migration success");
        
        // Test backup/restore
        var backupResult = await settings.CreateBackupAsync("test-backup");
        True(backupResult.Success, "backup success");
        NotNull(backupResult.BackupPath, "backup path");
        
        var restoreResult = await settings.RestoreBackupAsync(backupResult.BackupPath!);
        True(restoreResult.Success, "restore success");

        var permissions = new PermissionService(reopened, () => DateTimeOffset.UnixEpoch);
        var events = new List<PermissionAuditEvent>();
        permissions.Audited += (_, e) => events.Add(e);
        
        var request = new PermissionRequest("test.application", "files", "read", PermissionRisk.Consequential);
        Equal(PermissionDecisionKind.Ask, (await permissions.EvaluateAsync(request)).Kind, "ungranted consequential permission");
        
        await permissions.GrantAsync(request.SubjectId, request.Resource, request.Action, GrantSource.User);
        var allowedDecision = await permissions.EvaluateAsync(request);
        Equal(PermissionDecisionKind.Allowed, allowedDecision.Kind, "scoped grant");
        
        // Verify audit event was emitted
        True(events.Count >= 2, "audit events recorded");
        Equal(PermissionDecisionKind.Allowed, events[^1].Decision, "audit decision");
        
        await permissions.RevokeAsync(request.SubjectId, request.Resource, request.Action);
        Equal(PermissionDecisionKind.Ask, (await permissions.EvaluateAsync(request)).Kind, "revoked grant");
        
        // Test grant with expiration
        var future = DateTimeOffset.UtcNow.AddHours(1);
        await permissions.GrantAsync(request.SubjectId, request.Resource, request.Action, GrantSource.User, future);
        Equal(PermissionDecisionKind.Allowed, (await permissions.EvaluateAsync(request)).Kind, "grant with future expiration");
    }

    private async Task VerifyProviderRegistryAndGovernanceAsync()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var providers = new ProviderRegistry();
        
        var llamacppDescriptor = await providers.RegisterDescriptorAsync(Path.Combine(RepositoryRoot(), "HUI", "llamacpp-provider.json"));
        Equal("llamacpp", llamacppDescriptor.ProviderId, "descriptor provider id");
        Equal("http-over-unix", llamacppDescriptor.TransportType, "descriptor transport");
        Equal("ollama", llamacppDescriptor.LegacyUnqualifiedProvider, "descriptor legacy provider");
        Equal(ProviderKind.Local, llamacppDescriptor.Kind, "descriptor kind");
        True(llamacppDescriptor.TransportDetails.Count > 0, "transport details populated");
        True(llamacppDescriptor.Discovery.Count > 0, "discovery populated");
        
        var ollamaDescriptor = await providers.RegisterDescriptorAsync(Path.Combine(RepositoryRoot(), "HUI", "ollama-provider.json"));
        Equal("ollama", ollamaDescriptor.ProviderId, "ollama provider id");
        Equal("http", ollamaDescriptor.TransportType, "ollama transport");
        Equal(ProviderKind.Local, ollamaDescriptor.Kind, "ollama kind");
        
        var permissions = new PermissionService(settings);
        var governance = new ModelGovernance(providers, permissions, settings);
        await governance.SetFallbackOrderAsync(["llamacpp", "ollama"]);
        Equal("llamacpp", (await governance.GetFallbackOrderAsync())[0], "model fallback order first");
        Equal("ollama", (await governance.GetFallbackOrderAsync())[1], "model fallback order second");
        
        var request = new ModelUseRequest("llamacpp", "llamacpp:test-model", "chat", PermissionRisk.Consequential);
        Equal(PermissionDecisionKind.Ask, (await governance.EvaluateModelUseAsync(request)).Kind, "model use before grant");
        await permissions.GrantAsync("provider.llamacpp", "model.llamacpp:test-model", "chat");
        Equal(PermissionDecisionKind.Allowed, (await governance.EvaluateModelUseAsync(request)).Kind, "model use after central grant");
        
        // Test model key resolution (unqualified -> qualified)
        var resolvedKeys = await governance.ResolveModelKeyAsync("test-model");
        True(resolvedKeys.Count > 0, "unqualified model key resolved");
        True(resolvedKeys.Any(k => k.Contains("test-model")), "resolved key contains model name");
        
        // Test unregister
        True(await providers.UnregisterAsync("ollama"), "unregister ollama");
        False(providers.TryGet("ollama", out _), "ollama unregistered");
    }

    private async Task VerifyNotificationEventsAsync()
    {
        var settings = new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot));
        var notifications = new NotificationService(settings, () => DateTimeOffset.UnixEpoch);
        var events = new List<NotificationEvent>();
        notifications.Changed += (_, notificationEvent) => events.Add(notificationEvent);

        var actions = new[] { new NotificationAction("dismiss", "Dismiss"), new NotificationAction("open", "Open", true) };
        var notification = await notifications.PublishAsync(new NotificationDraft("test.application", NotificationSeverity.Information, "Title", "Body", actions, NotificationPersistencePolicy.Persistent));
        Equal(1, (await notifications.GetActiveAsync()).Count, "active notification count");
        Equal(NotificationEventKind.Published, events[0].Kind, "published notification event");
        Equal(2, events[0].Notification.Actions.Count, "notification actions preserved");
        
        True(await notifications.MarkReadAsync(notification.Id), "notification mark read");
        Equal(NotificationEventKind.Read, events[1].Kind, "read notification event");
        NotNullStruct(events[1].Notification.ReadAtUtc, "read timestamp set");
        
        True(await notifications.DismissAsync(notification.Id), "notification dismissal");
        Equal(0, (await notifications.GetActiveAsync()).Count, "dismissed notification count");
        Equal(NotificationEventKind.Dismissed, events[2].Kind, "dismissed notification event");
        NotNullStruct(events[2].Notification.DismissedAtUtc, "dismissed timestamp set");
        
        // Test history includes dismissed
        var history = await notifications.GetHistoryAsync(10);
        Equal(1, history.Count, "history includes dismissed notification");
        
        // Test clear dismissed
        await notifications.PublishAsync(new NotificationDraft("test.application", NotificationSeverity.Information, "Title2", "Body2"));
        await notifications.DismissAsync((await notifications.GetHistoryAsync(1))[0].Id);
        var cleared = await notifications.ClearDismissedAsync();
        Equal(1, cleared, "cleared dismissed count");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "havenos.lock")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the CakeOS repository root.");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    }

    private static void NotNull<T>(T? value, string name) where T : class
    {
        if (value is null)
            throw new InvalidOperationException($"{name}: expected non-null.");
    }

    private static void NotNullStruct<T>(T? value, string name) where T : struct
    {
        if (!value.HasValue)
            throw new InvalidOperationException($"{name}: expected non-null.");
    }

    private static void True(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"{name}: expected true.");
    }

    private static void False(bool condition, string name)
    {
        if (condition)
            throw new InvalidOperationException($"{name}: expected false.");
    }

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
    }

    private sealed record TestSetting(string Value);
    private sealed class TestRootElement : IRootElement { }
}
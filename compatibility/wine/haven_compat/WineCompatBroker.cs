using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HavenOS.Compat.Wine;

public sealed record WineAppManifest(
    string AppId,
    string DisplayName,
    string Backend,
    string Runtime,
    string Entrypoint,
    string Network = "none",
    bool Clipboard = false,
    bool AudioOutput = false,
    bool Microphone = false,
    string Gpu = "none",
    IReadOnlyList<WineMount> Mounts = null!)
{
    [JsonConstructor]
    public WineAppManifest(string AppId, string DisplayName, string Backend, string Runtime, string Entrypoint,
        string Network = "none", bool Clipboard = false, bool AudioOutput = false, bool Microphone = false,
        string Gpu = "none", IReadOnlyList<WineMount> Mounts = null!)
    {
        this.AppId = AppId;
        this.DisplayName = DisplayName;
        this.Backend = Backend;
        this.Runtime = Runtime;
        this.Entrypoint = Entrypoint;
        this.Network = Network;
        this.Clipboard = Clipboard;
        this.AudioOutput = AudioOutput;
        this.Microphone = Microphone;
        this.Gpu = Gpu;
        this.Mounts = Mounts ?? [];
    }
}

public sealed record WineMount(string Source, string Target, string Mode = "ro");

public sealed record LaunchPlan(string Backend, IReadOnlyList<string> Argv, IReadOnlyDictionary<string, string> Env, string PrefixPath);

public sealed record WineCapabilities(
    int SchemaVersion,
    WineProviderCapabilities Providers);

public sealed record WineProviderCapabilities(
    WineProvider Wine,
    WineProvider WinBoat);

public sealed record WineProvider(
    bool Enabled,
    int Slice,
    IReadOnlyList<string> Network,
    IReadOnlyList<string> Display,
    IReadOnlyList<string> Filesystem,
    IReadOnlyList<string> Gpu,
    bool Clipboard,
    bool AudioOutput,
    bool Microphone,
    IReadOnlyList<string> Lifecycle);

public sealed record WineHealth(
    int SchemaVersion,
    WineAudit Audit,
    WineSupervisor Supervisor);

public sealed record WineAudit(
    bool WineAvailable,
    string WineVersion,
    bool BubblewrapAvailable,
    bool WaylandAvailable,
    IReadOnlyList<string> RenderNodes);

public sealed record WineSupervisor(
    string Type,
    bool Available);

public sealed record UnitStatus(
    bool Running,
    int? Pid,
    string State,
    string Unit);

public sealed record WineAppSummary(
    string Id,
    string DisplayName,
    string Backend,
    string Runtime,
    string Entrypoint,
    string Unit,
    WinePermissions Permissions);

public sealed record WinePermissions(
    string Network,
    bool Clipboard,
    bool AudioOutput,
    bool Microphone,
    string Gpu,
    IReadOnlyList<WineMount> Mounts);

public sealed class WineCompatBroker
{
    private readonly string _stateRoot;
    private readonly string _runtimeRoot;
    private readonly string _pythonBrokerPath;

    public WineCompatBroker(string? stateRoot = null, string? runtimeRoot = null, string? pythonBrokerPath = null)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        _stateRoot = stateRoot ?? Path.Combine(dataHome, "haven", "compat", "wine", "apps");
        _runtimeRoot = runtimeRoot ?? Path.Combine(dataHome, "haven", "compat", "wine", "runtimes");
        _pythonBrokerPath = pythonBrokerPath ?? Path.Combine(AppContext.BaseDirectory, "..", "compatibility", "wine", "haven_compat", "broker.py");
        _pythonBrokerPath = Path.GetFullPath(_pythonBrokerPath);
    }

    public WineCapabilities Capabilities()
    {
        return new WineCapabilities(1, new WineProviderCapabilities(
            new WineProvider(true, 1, ["none"], ["wayland"], ["none", "explicit-ro", "explicit-rw"], ["none", "render"], false, false, false, ["launch", "status", "stop", "logs", "reset"]),
            new WineProvider(false, 0, [], [], [], [], false, false, false, [])
        ));
    }

    public async Task<WineHealth> HealthAsync()
    {
        var audit = await AuditEnvironmentAsync();
        var supervisor = new WineSupervisor("systemd-user", IsSystemdUserAvailable());
        return new WineHealth(1, audit, supervisor);
    }

    public async Task<WineAppSummary> RegisterAppAsync(WineAppManifest manifest)
    {
        var registryPath = Path.Combine(Path.GetDirectoryName(_pythonBrokerPath)!, "..", "registry");
        registryPath = Path.GetFullPath(registryPath);
        
        var manifestPath = Path.Combine(registryPath, "manifests", manifest.AppId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json);

        return CreateAppSummary(manifest);
    }

    public async Task<IReadOnlyList<WineAppSummary>> ListAppsAsync()
    {
        var registryPath = Path.Combine(Path.GetDirectoryName(_pythonBrokerPath)!, "..", "registry", "manifests");
        registryPath = Path.GetFullPath(registryPath);
        
        if (!Directory.Exists(registryPath))
            return [];

        var files = Directory.GetFiles(registryPath, "*.json");
        var apps = new List<WineAppSummary>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file);
            var manifest = JsonSerializer.Deserialize<WineAppManifest>(json)!;
            apps.Add(CreateAppSummary(manifest));
        }
        return apps;
    }

    public async Task<WineAppManifest> GetRegisteredAppAsync(string appId)
    {
        var registryPath = Path.Combine(Path.GetDirectoryName(_pythonBrokerPath)!, "..", "registry", "manifests");
        registryPath = Path.GetFullPath(registryPath);
        
        var manifestPath = Path.Combine(registryPath, appId + ".json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"App not found: {appId}");

        var json = await File.ReadAllTextAsync(manifestPath);
        return JsonSerializer.Deserialize<WineAppManifest>(json)!;
    }

    public async Task<WineAppSummary> UnregisterAppAsync(string appId, bool deleteState = false)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        var status = await StatusAsync(manifest);
        if (status.Running)
            throw new InvalidOperationException("Refusing to unregister a running compatibility application; stop it first");

        if (deleteState)
            await ResetAsync(manifest);

        var registryPath = Path.Combine(Path.GetDirectoryName(_pythonBrokerPath)!, "..", "registry", "manifests");
        registryPath = Path.GetFullPath(registryPath);
        var manifestPath = Path.Combine(registryPath, appId + ".json");
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        return CreateAppSummary(manifest) with { Id = appId };
    }

    public async Task<LaunchPlan> PlanAsync(WineAppManifest manifest)
    {
        if (manifest.Backend == "winboat")
            throw new NotSupportedException("WinBoat provider is not enabled in compatibility slice 1");
        if (manifest.Backend != "wine")
            throw new NotSupportedException($"Unsupported backend: {manifest.Backend}");
        if (manifest.Network != "none")
            throw new NotSupportedException("Network permissions are not implemented in compatibility slice 1");
        if (manifest.Clipboard)
            throw new NotSupportedException("Clipboard permission is not implemented in compatibility slice 1");
        if (manifest.AudioOutput || manifest.Microphone)
            throw new NotSupportedException("PipeWire media permissions are not implemented in compatibility slice 1");

        var bwrap = FindExecutable("bwrap");
        if (string.IsNullOrEmpty(bwrap))
            throw new InvalidOperationException("bubblewrap is required; refusing unsandboxed Wine execution");

        var runtimeDir = Path.Combine(_runtimeRoot, manifest.Runtime);
        var wineBin = Path.Combine(runtimeDir, "bin", "wine");
        if (!File.Exists(wineBin))
            throw new InvalidOperationException($"Wine runtime is unavailable: {wineBin}");

        var (xdgRuntimeDir, waylandDisplay, waylandSocket) = GetWaylandSocket();

        var appRoot = Path.Combine(_stateRoot, manifest.AppId);
        var prefix = Path.Combine(appRoot, "prefix");
        var data = Path.Combine(appRoot, "data");
        Directory.CreateDirectory(prefix);
        Directory.CreateDirectory(data);

        var argv = new List<string>
        {
            bwrap,
            "--die-with-parent",
            "--new-session",
            "--unshare-all",
            "--proc", "/proc",
            "--dev", "/dev",
            "--tmpfs", "/tmp",
            "--dir", xdgRuntimeDir,
            "--dir", "/mnt",
            "--dir", "/mnt/haven-share",
            "--ro-bind", runtimeDir, "/opt/haven-wine",
            "--bind", prefix, "/var/lib/haven-wine/prefix",
            "--bind", data, "/var/lib/haven-wine/data",
            "--ro-bind", waylandSocket, waylandSocket,
        };

        foreach (var hostPath in new[] { "/usr", "/lib", "/lib64" })
        {
            if (Directory.Exists(hostPath))
            {
                argv.Add("--ro-bind");
                argv.Add(hostPath);
                argv.Add(hostPath);
            }
        }

        foreach (var mount in manifest.Mounts)
        {
            var source = ValidateMountSource(mount.Source);
            var flag = mount.Mode == "ro" ? "--ro-bind" : "--bind";
            argv.Add(flag);
            argv.Add(source);
            argv.Add(mount.Target);
        }

        if (manifest.Gpu == "render")
        {
            var renderNodes = GetRenderNodes();
            if (renderNodes.Count == 0)
                throw new InvalidOperationException("GPU render permission requested but no render node is available");
            foreach (var node in renderNodes)
            {
                argv.Add("--dev-bind");
                argv.Add(node);
                argv.Add(node);
            }
        }

        argv.AddRange(new[]
        {
            "--setenv", "WINEPREFIX", "/var/lib/haven-wine/prefix",
            "--setenv", "HOME", "/var/lib/haven-wine/data",
            "--setenv", "XDG_RUNTIME_DIR", xdgRuntimeDir,
            "--setenv", "WAYLAND_DISPLAY", waylandDisplay,
            "/opt/haven-wine/bin/wine",
            manifest.Entrypoint,
        });

        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin:/bin",
            ["LANG"] = Environment.GetEnvironmentVariable("LANG") ?? "C.UTF-8",
        };

        return new LaunchPlan("wine", argv, env, prefix);
    }

    public async Task<UnitStatus> LaunchAsync(WineAppManifest manifest)
    {
        var plan = await PlanAsync(manifest);
        return StartSystemdUnit(manifest.AppId, plan.Argv, plan.Env);
    }

    public async Task<UnitStatus> LaunchRegisteredAsync(string appId)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        return await LaunchAsync(manifest);
    }

    public async Task<UnitStatus> StatusAsync(WineAppManifest manifest)
    {
        return GetSystemdUnitStatus(manifest.AppId);
    }

    public async Task<UnitStatus> StatusRegisteredAsync(string appId)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        return await StatusAsync(manifest);
    }

    public async Task<UnitStatus> StopAsync(WineAppManifest manifest)
    {
        return StopSystemdUnit(manifest.AppId);
    }

    public async Task<UnitStatus> StopRegisteredAsync(string appId)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        return await StopAsync(manifest);
    }

    public async Task<string> LogsAsync(WineAppManifest manifest, int lines = 200)
    {
        return GetSystemdUnitLogs(manifest.AppId, lines);
    }

    public async Task<string> LogsRegisteredAsync(string appId, int lines = 200)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        return await LogsAsync(manifest, lines);
    }

    public async Task ResetAsync(WineAppManifest manifest)
    {
        if (manifest.Backend != "wine")
            throw new NotSupportedException("Environment reset is only implemented for the Wine provider");
        
        var status = await StatusAsync(manifest);
        if (status.Running)
            throw new InvalidOperationException("Refusing to reset a running compatibility application; stop it first");

        var appRoot = Path.Combine(_stateRoot, manifest.AppId);
        if (Directory.Exists(appRoot))
            Directory.Delete(appRoot, true);
    }

    public async Task ResetRegisteredAsync(string appId)
    {
        var manifest = await GetRegisteredAppAsync(appId);
        await ResetAsync(manifest);
    }

    public string LifecycleUnit(WineAppManifest manifest) => $"haven-wine-{manifest.AppId}.service";

    private WineAppSummary CreateAppSummary(WineAppManifest manifest) => new(
        manifest.AppId, manifest.DisplayName, manifest.Backend, manifest.Runtime, manifest.Entrypoint,
        LifecycleUnit(manifest),
        new WinePermissions(manifest.Network, manifest.Clipboard, manifest.AudioOutput, manifest.Microphone, manifest.Gpu, manifest.Mounts));

    private async Task<WineAudit> AuditEnvironmentAsync()
    {
        var wineAvailable = !string.IsNullOrEmpty(FindExecutable("wine"));
        string wineVersion = "";
        if (wineAvailable)
        {
            try
            {
                var psi = new ProcessStartInfo("wine", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
                using var proc = Process.Start(psi);
                wineVersion = (await proc!.StandardOutput.ReadToEndAsync()).Trim();
            }
            catch { }
        }

        var bubblewrapAvailable = !string.IsNullOrEmpty(FindExecutable("bwrap"));
        var waylandAvailable = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var renderNodes = GetRenderNodes();

        return new WineAudit(wineAvailable, wineVersion, bubblewrapAvailable, waylandAvailable, renderNodes);
    }

    private static string? FindExecutable(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, name);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }

    private static bool IsSystemdUserAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("systemctl", "--user status") { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            proc!.WaitForExit(1000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static (string RuntimeDir, string Display, string Socket) GetWaylandSocket()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(runtimeDir) || string.IsNullOrEmpty(display))
            throw new InvalidOperationException("Wayland session is required in compatibility slice 1");
        var socket = Path.Combine(runtimeDir, display);
        if (!File.Exists(socket))
            throw new InvalidOperationException($"Wayland socket is unavailable: {socket}");
        return (runtimeDir, display, socket);
    }

    private static List<string> GetRenderNodes()
    {
        var dri = new DirectoryInfo("/dev/dri");
        if (!dri.Exists) return [];
        var nodes = new List<string>();
        foreach (var file in dri.GetFiles("renderD*"))
        {
            if ((file.Attributes & FileAttributes.Device) != 0)
                nodes.Add(file.FullName);
        }
        return nodes;
    }

    private string ValidateMountSource(string source)
    {
        var resolved = Path.GetFullPath(source);
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
            throw new ArgumentException($"Mount source does not exist: {source}");

        var home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var forbidden = new[]
        {
            Path.GetFullPath("/"), Path.GetFullPath("/home"), home, Path.GetFullPath("/tmp"),
            Path.GetFullPath("/etc"), Path.GetFullPath("/proc"), Path.GetFullPath("/sys"),
            Path.GetFullPath("/dev"), Path.GetFullPath("/boot"), Path.GetFullPath("/usr"),
            Path.GetFullPath("/bin"), Path.GetFullPath("/sbin"), Path.GetFullPath("/lib"),
            Path.GetFullPath("/lib64"), Path.GetFullPath("/var"), Path.GetFullPath("/run"),
            Path.GetFullPath("/root")
        }.Where(d => Directory.Exists(d)).Select(Path.GetFullPath).ToArray();

        if (forbidden.Any(f => resolved == f || resolved.StartsWith(f + Path.DirectorySeparatorChar)))
            throw new ArgumentException($"Refusing sensitive host path grant: {resolved}");

        var compatRoot = Path.GetFullPath(Path.Combine(_stateRoot, ".."));
        var runtimeRoot = Path.GetFullPath(_runtimeRoot);
        if (resolved == compatRoot || resolved.StartsWith(compatRoot + Path.DirectorySeparatorChar) ||
            resolved == runtimeRoot || resolved.StartsWith(runtimeRoot + Path.DirectorySeparatorChar))
            throw new ArgumentException("Refusing access to compatibility backend state");

        return resolved;
    }

    private UnitStatus StartSystemdUnit(string appId, IReadOnlyList<string> argv, IReadOnlyDictionary<string, string> env)
    {
        var unitName = $"haven-wine-{appId}.service";
        var unitContent = $@"[Unit]
Description=Haven Wine App: {appId}
After=graphical-session.target

[Service]
Type=exec
ExecStart={' '.join(argv.Select(EscapeSystemdArg))}
Environment={string.Join(" ", env.Select(kvp => $"{kvp.Key}={EscapeSystemdArg(kvp.Value)}"))}
Restart=no

[Install]
WantedBy=default.target
";
        var unitPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "systemd", "user", unitName);
        Directory.CreateDirectory(Path.GetDirectoryName(unitPath)!);
        File.WriteAllText(unitPath, unitContent);

        RunSystemctl("--user", "daemon-reload");
        RunSystemctl("--user", "start", unitName);
        return GetSystemdUnitStatus(appId);
    }

    private UnitStatus StopSystemdUnit(string appId)
    {
        var unitName = $"haven-wine-{appId}.service";
        RunSystemctl("--user", "stop", unitName);
        return GetSystemdUnitStatus(appId);
    }

    private UnitStatus GetSystemdUnitStatus(string appId)
    {
        var unitName = $"haven-wine-{appId}.service";
        var output = RunSystemctl("--user", "show", unitName, "--property=ActiveState,SubState,MainPID");
        var lines = output.Split('\n');
        string state = "inactive";
        int? pid = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("ActiveState=")) state = line.Split('=')[1];
            else if (line.StartsWith("MainPID=") && int.TryParse(line.Split('=')[1], out var p) && p > 0) pid = p;
        }
        return new UnitStatus(state == "active", pid, state, unitName);
    }

    private string GetSystemdUnitLogs(string appId, int lines)
    {
        var unitName = $"haven-wine-{appId}.service";
        return RunSystemctl("--user", "status", unitName, "-n", lines.ToString(), "--no-pager");
    }

    private string RunSystemctl(params string[] args)
    {
        var psi = new ProcessStartInfo("systemctl", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var proc = Process.Start(psi);
        var output = proc!.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }

    private static string EscapeSystemdArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
            return '"' + arg.Replace("\"", "\\\"") + '"';
        return arg;
    }

    public void Dispose() { }
}
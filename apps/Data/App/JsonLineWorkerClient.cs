using System.Diagnostics;
using System.Text.Json;

namespace HavenOS.Apps.Data;

internal sealed class JsonLineWorkerClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private int _nextId;

    public JsonLineWorkerClient(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;

        _process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start worker '{executable}'.");
    }

    public async Task<T> CallAsync<T>(string method, object? parameters, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Worker exited with code {_process.ExitCode}: {await _process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false)}");

            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new { id, method, @params = parameters }, _json);
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Worker closed stdout before replying.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                throw new InvalidDataException("Worker response id did not match the request.");
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(error.GetString() ?? "Worker operation failed.");
            if (!root.TryGetProperty("result", out var result))
                throw new InvalidDataException("Worker response did not contain a result.");
            return result.Deserialize<T>(_json) ?? throw new InvalidDataException("Worker returned an empty result.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try { await CallAsync<object>("shutdown", null, CancellationToken.None).ConfigureAwait(false); }
                catch { _process.Kill(entireProcessTree: true); }
            }
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }
    }
}

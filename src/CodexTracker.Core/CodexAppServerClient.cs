using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexTracker.Core;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private readonly string _executable;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = [];
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private long _nextId;
    private int _terminated;
    public event Action<RateLimitSnapshot>? SnapshotUpdated;
    public event Action<string>? StatusChanged;
    public RateLimitSnapshot? Snapshot { get; private set; }

    public CodexAppServerClient(string executable) => _executable = executable;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _process = new Process { StartInfo = new ProcessStartInfo(_executable, "app-server --listen stdio://") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => TerminatePending(new InvalidOperationException("Codex app-server exited."));
        _process.Start();
        _ = Task.Run(() => ReadLoopAsync(_lifetime.Token));
        _ = Task.Run(() => DrainErrorAsync(_lifetime.Token));
        StatusChanged?.Invoke("Connecting to Codex");
        await RequestAsync("initialize", new { clientInfo = new { name = "codex-tracker", version = "0.3.0" }, capabilities = new { } }, cancellationToken);
        await NotifyAsync("initialized", new { }, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync("account/rateLimits/read", null, cancellationToken);
        ApplySnapshot(RateLimitParser.Parse(result, DateTimeOffset.UtcNow), false);
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = source;
        await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime?.Token ?? CancellationToken.None);
        timeout.CancelAfter(RequestTimeout);
        using var registration = timeout.Token.Register(() => source.TrySetException(new TimeoutException($"Timed out waiting for Codex {method}.")));
        try { return await source.Task; }
        finally { lock (_pending) _pending.Remove(id); }
    }

    private Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) => SendAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);
    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        if (_process is null) throw new InvalidOperationException("Codex app-server is not running.");
        await _writeLock.WaitAsync(cancellationToken);
        try { await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(payload)); await _process.StandardInput.FlushAsync(); }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_process is { HasExited: false } && !cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var id))
                {
                    TaskCompletionSource<JsonElement>? source;
                    lock (_pending) { _pending.Remove(id, out source); }
                    if (source is null) continue;
                    if (message.TryGetProperty("error", out var error)) source.TrySetException(new InvalidOperationException("Codex rejected the request: " + error.GetRawText()));
                    else if (message.TryGetProperty("result", out var result)) source.TrySetResult(result.Clone());
                    continue;
                }
                if (message.TryGetProperty("method", out var method) && method.GetString() == "account/rateLimits/updated" && message.TryGetProperty("params", out var update))
                    ApplySnapshot(RateLimitParser.Parse(update, DateTimeOffset.UtcNow), true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TerminatePending(ex); StatusChanged?.Invoke("Connection interrupted: " + ex.Message); }
        finally { if (!cancellationToken.IsCancellationRequested) TerminatePending(new InvalidOperationException("Codex app-server output closed.")); }
    }

    private async Task DrainErrorAsync(CancellationToken cancellationToken)
    {
        try { while (_process is { HasExited: false } && !cancellationToken.IsCancellationRequested) _ = await _process.StandardError.ReadLineAsync(cancellationToken); }
        catch (OperationCanceledException) { }
    }

    private void ApplySnapshot(RateLimitSnapshot next, bool sparse)
    {
        Snapshot = sparse && Snapshot is not null ? RateLimitParser.Merge(Snapshot, next) : next;
        StatusChanged?.Invoke("Live from Codex");
        SnapshotUpdated?.Invoke(Snapshot);
    }
    private void TerminatePending(Exception exception)
    {
        if (Interlocked.Exchange(ref _terminated, 1) == 1) return;
        List<TaskCompletionSource<JsonElement>> pending;
        lock (_pending) { pending = _pending.Values.ToList(); _pending.Clear(); }
        foreach (var request in pending) request.TrySetException(exception);
        StatusChanged?.Invoke("Connection interrupted: " + exception.Message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null) await _lifetime.CancelAsync();
        TerminatePending(new OperationCanceledException("Codex Tracker is closing."));
        if (_process is { HasExited: false }) { try { _process.Kill(true); } catch { } }
        _process?.Dispose(); _lifetime?.Dispose(); _writeLock.Dispose();
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThesisDemo.Infrastructure.Legacy;

public sealed record PythonSearchProcessOptions(
    string PythonExecutable,
    string WorkerScriptPath,
    string? WorkingDirectory = null,
    TimeSpan? RequestTimeout = null,
    TimeSpan? RestartTimeout = null);

public sealed record SearchWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    string Status,
    string? IndexId,
    string? ModelId,
    SearchEffectiveConfig? EffectiveConfig,
    long? ElapsedMs,
    IReadOnlyList<SearchWorkerResult>? BaselineResults,
    IReadOnlyList<SearchWorkerResult>? RerankedResults,
    IReadOnlyList<string>? Warnings,
    SearchWorkerError? Error);

public sealed record SearchEffectiveConfig(int TopK, int CandidateK, double HybridAlpha);

public sealed record SearchWorkerResult(
    string AssetId,
    string SegmentId,
    double StartSec,
    double EndSec,
    string Caption,
    string? TranscriptText,
    string? OcrText,
    double Score,
    string ScoreKind);

public sealed record SearchWorkerError(string Code, string Message, bool Retryable);

public sealed class PythonSearchException(string message, string? code = null, bool retryable = false) : Exception(message)
{
    public string? Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class PythonSearchProcess(PythonSearchProcessOptions options) : IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _stderr = new();
    private Process? _process;
    private Task? _stderrTask;

    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            StartProcess();
            await PingAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SearchWorkerResponse> LoadIndexAsync(string indexId, string indexPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        return await SendAsync(new
        {
            protocolVersion = ProtocolVersion,
            requestId = NewRequestId("load"),
            operation = "loadIndex",
            indexId,
            indexPath
        }, cancellationToken);
    }

    public async Task<SearchWorkerResponse> WarmAsync(CancellationToken cancellationToken = default) =>
        await SendAsync(new
        {
            protocolVersion = ProtocolVersion,
            requestId = NewRequestId("warm"),
            operation = "warm"
        }, cancellationToken);

    public async Task<SearchWorkerResponse> SearchAsync(string indexId, string query, int topK, int candidateK,
        double hybridAlpha, IReadOnlyList<string>? assetIds = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return await SendAsync(new
        {
            protocolVersion = ProtocolVersion,
            requestId = NewRequestId("search"),
            operation = "search",
            indexId,
            query,
            topK,
            candidateK,
            hybridAlpha,
            mode = "baseline",
            assetIds = assetIds ?? []
        }, cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopProcessAsync();
            StartProcess();
            await PingAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopProcessAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<SearchWorkerResponse> SendAsync(object request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsRunning)
            {
                StartProcess();
                await PingAsync(cancellationToken);
            }

            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout ?? TimeSpan.FromSeconds(10));

            try
            {
                await _process!.StandardInput.WriteLineAsync(json.AsMemory(), timeout.Token);
                await _process.StandardInput.FlushAsync(timeout.Token);
                var line = await _process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    await StopProcessAsync();
                    throw new PythonSearchException("Search worker exited before it returned a response.", "WORKER_EXITED", true);
                }

                var response = JsonSerializer.Deserialize<SearchWorkerResponse>(line, JsonOptions)
                    ?? throw new PythonSearchException("Search worker returned an empty response.", "INVALID_RESPONSE", true);
                if (response.Status == "error")
                    throw new PythonSearchException(response.Error?.Message ?? "Search worker failed.",
                        response.Error?.Code, response.Error?.Retryable ?? false);
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await StopProcessAsync();
                throw new TimeoutException("Search worker request timed out.");
            }
            catch (IOException ex)
            {
                await StopProcessAsync();
                throw new PythonSearchException($"Search worker pipe failed: {ex.Message}", "PIPE_FAILED", true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new
        {
            protocolVersion = ProtocolVersion,
            requestId = NewRequestId("ping"),
            operation = "ping"
        }, JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RestartTimeout ?? TimeSpan.FromSeconds(5));
        await _process!.StandardInput.WriteLineAsync(json.AsMemory(), timeout.Token);
        await _process.StandardInput.FlushAsync(timeout.Token);
        var responseLine = await _process.StandardOutput.ReadLineAsync(timeout.Token)
            ?? throw new PythonSearchException("Search worker did not respond to ping.", "WORKER_EXITED", true);
        var response = JsonSerializer.Deserialize<SearchWorkerResponse>(responseLine, JsonOptions)
            ?? throw new PythonSearchException("Search worker returned an invalid ping response.", "INVALID_RESPONSE", true);
        if (response.Status == "error")
            throw new PythonSearchException(response.Error?.Message ?? "Search worker ping failed.", response.Error?.Code, response.Error?.Retryable ?? true);
    }

    private void StartProcess()
    {
        var workerPath = Path.GetFullPath(options.WorkerScriptPath);
        if (!File.Exists(workerPath)) throw new FileNotFoundException("Search worker script was not found.", workerPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = options.PythonExecutable,
            WorkingDirectory = options.WorkingDirectory is null
                ? Path.GetDirectoryName(workerPath)!
                : Path.GetFullPath(options.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(workerPath);
        _stderr.Clear();
        var startedProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Search worker process could not start.");
        _process = startedProcess;
        _stderrTask = Task.Run(async () =>
        {
            while (!startedProcess.HasExited)
            {
                var line = await startedProcess.StandardError.ReadLineAsync();
                if (line is null) break;
                if (_stderr.Length < 16_384) _stderr.AppendLine(line);
            }
        });
    }

    private async Task StopProcessAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(options.RestartTimeout ?? TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            if (_stderrTask is not null) await _stderrTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            process.Dispose();
        }
    }

    private bool IsRunning => _process is { HasExited: false };
    private static string NewRequestId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}

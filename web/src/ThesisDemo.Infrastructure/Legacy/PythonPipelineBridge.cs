using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Processing;
using ThesisDemo.Infrastructure.Jobs;

namespace ThesisDemo.Infrastructure.Legacy;

public sealed record PipelineAsset(Guid Id, string LegacyId, string MediaPath);
public sealed record PipelineOutcome(int ExitCode, IReadOnlyDictionary<Guid, bool> Outcomes, string OutputRoot);

public sealed class PythonPipelineBridge(IConfiguration configuration, IDurableJobRepository jobs)
{
    public async Task<PipelineOutcome> ExecuteAsync(Guid jobId, Guid runId, ProcessingSnapshot snapshot,
        IReadOnlyList<PipelineAsset> assets, string managedRoot, CancellationToken ct)
    {
        var python = configuration["Processing:PythonExecutable"];
        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python))
            throw new InvalidOperationException("Processing:PythonExecutable must point to a configured Python interpreter.");
        var repo = FindRoot();
        var bridge = Path.Combine(repo, "web", "bridge", "job_runner.py");
        var pipeline = Path.Combine(repo, "legacy-pipeline");
        var root = Path.Combine(managedRoot, "processing", runId.ToString("N"));
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var mapping = new Dictionary<string, string>();
        foreach (var asset in assets)
        {
            var stem = asset.Id.ToString("N");
            var dest = Path.Combine(input, stem + Path.GetExtension(asset.MediaPath).ToLowerInvariant());
            if (!File.Exists(dest)) File.Copy(asset.MediaPath, dest);
            mapping[asset.Id.ToString("D")] = stem;
        }
        var requestPath = Path.Combine(root, "request.json");
        await File.WriteAllTextAsync(requestPath, ProcessingValidation.Serialize(new
        {
            protocolVersion = 1, python, input, output, pipelineRoot = pipeline,
            manifest = Path.Combine(root, "manifest.jsonl"), log = Path.Combine(root, "pipeline.log"),
            assets = mapping, settings = snapshot.Settings,
            captionModel = snapshot.CaptionModelId, embeddingModel = snapshot.EmbeddingModelId
        }), ct);
        var info = new ProcessStartInfo(python)
        {
            WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add(bridge);
        info.ArgumentList.Add(requestPath);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Pipeline runner did not start.");
        var errors = process.StandardError.ReadToEndAsync(ct);
        var outcomes = new Dictionary<Guid, bool>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(configuration.GetValue("Processing:TimeoutMinutes", 120)));
        try
        {
            var pending = process.StandardOutput.ReadLineAsync(timeout.Token).AsTask();
            while (true)
            {
                if (await Task.WhenAny(pending, Task.Delay(500, timeout.Token)) == pending)
                {
                    var line = await pending;
                    if (line is null) break;
                    using var document = JsonDocument.Parse(line);
                    var evt = document.RootElement;
                    if (evt.GetProperty("protocolVersion").GetInt32() != 1) throw new InvalidDataException("Unexpected pipeline protocol.");
                    var kind = evt.GetProperty("kind").GetString()!;
                    if (kind is "asset_completed" or "asset_failed")
                    {
                        var assetId = evt.GetProperty("assetId").GetGuid();
                        if (!assets.Any(a => a.Id == assetId) || outcomes.ContainsKey(assetId))
                            throw new InvalidDataException("Unknown or duplicated pipeline asset outcome.");
                        outcomes[assetId] = kind == "asset_completed";
                    }
                    await jobs.RecordAsync(jobId, kind, JsonSerializer.Serialize(new
                    {
                        completedAssets = outcomes.Count(x => x.Value), failedAssets = outcomes.Count(x => !x.Value),
                        totalAssets = assets.Count, assetId = evt.TryGetProperty("assetId", out var id) ? id.GetString() : null
                    }), ct);
                    pending = process.StandardOutput.ReadLineAsync(timeout.Token).AsTask();
                }
                if (await jobs.IsCancellationRequestedAsync(jobId, ct)) throw new OperationCanceledException("Cancellation requested.");
            }
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            return new PipelineOutcome(process.ExitCode, outcomes, output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "legacy-pipeline")) &&
                Directory.Exists(Path.Combine(dir.FullName, "web"))) return dir.FullName;
        throw new DirectoryNotFoundException("The pipeline repository was not found.");
    }
}

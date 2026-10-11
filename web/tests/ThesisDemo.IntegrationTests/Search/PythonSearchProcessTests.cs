using System.Diagnostics;
using System.Text.Json;
using ThesisDemo.Infrastructure.Legacy;

namespace ThesisDemo.IntegrationTests.Search;

public sealed class PythonSearchProcessTests
{
    [Fact]
    public async Task Search_worker_returns_ranked_results_and_restarts_after_crash()
    {
        var root = FindRepositoryRoot();
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"thesis-search-{Guid.NewGuid():N}"));
        var indexPath = Path.Combine(temp.FullName, "combined_index.json");
        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(new object[]
        {
            new
            {
                asset_id = "clip_a",
                segment_id = "seg_0001",
                start_sec = 0.0,
                end_sec = 5.0,
                caption = "rocket launch over ocean",
                transcript_text = (string?)null,
                ocr_text = (string?)null,
                summary = (string?)null,
                search_text = "rocket launch over ocean",
                embedding = new[] { 0.1, 0.2 },
                source_video = "private-a.mp4"
            },
            new
            {
                asset_id = "clip_b",
                segment_id = "seg_0001",
                start_sec = 0.0,
                end_sec = 5.0,
                caption = "quiet forest trail",
                transcript_text = (string?)null,
                ocr_text = (string?)null,
                summary = (string?)null,
                search_text = "quiet forest trail",
                embedding = new[] { 0.2, 0.1 },
                source_video = "private-b.mp4"
            }
        }));

        await using var process = new PythonSearchProcess(new PythonSearchProcessOptions(
            PythonExecutable: ResolvePython(),
            WorkerScriptPath: Path.Combine(root, "web", "bridge", "search_worker.py"),
            WorkingDirectory: root,
            RequestTimeout: TimeSpan.FromSeconds(5),
            RestartTimeout: TimeSpan.FromSeconds(5)));

        await process.StartAsync();
        var load = await process.LoadIndexAsync("fixture", indexPath);
        Assert.Equal("fixture", load.IndexId);

        var first = await process.SearchAsync("fixture", "rocket ocean", topK: 1, candidateK: 2, hybridAlpha: 0.6);
        var result = Assert.Single(first.BaselineResults!);
        Assert.Equal("clip_a", result.AssetId);
        Assert.Null(first.RerankedResults);

        var childProcessId = process.ProcessId;
        Assert.NotNull(childProcessId);
        Process.GetProcessById(childProcessId.Value).Kill(entireProcessTree: true);
        await Assert.ThrowsAnyAsync<Exception>(() => process.SearchAsync("fixture", "rocket", topK: 1, candidateK: 2, hybridAlpha: 0.6));

        await process.RestartAsync();
        await process.LoadIndexAsync("fixture", indexPath);
        var afterRestart = await process.SearchAsync("fixture", "forest", topK: 1, candidateK: 2, hybridAlpha: 0.6);
        Assert.Equal("clip_b", Assert.Single(afterRestart.BaselineResults!).AssetId);
    }

    private static string ResolvePython()
    {
        foreach (var candidate in new[] { "python", "py" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                if (process is null) continue;
                process.WaitForExit(5_000);
                if (process.ExitCode == 0) return candidate;
            }
            catch
            {
                // Try the next common launcher.
            }
        }
        throw new InvalidOperationException("Python executable was not found.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "web")) && Directory.Exists(Path.Combine(current.FullName, "specs")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

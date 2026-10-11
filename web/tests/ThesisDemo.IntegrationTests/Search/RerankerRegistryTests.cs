using System.Diagnostics;
using ThesisDemo.Infrastructure.Legacy;

namespace ThesisDemo.IntegrationTests.Search;

public sealed class RerankerRegistryTests
{
    [Fact]
    public async Task Reranker_registry_and_python_adapter_validate_feature_schema()
    {
        var root = FindRepositoryRoot();
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"thesis-reranker-{Guid.NewGuid():N}"));
        var validPath = Path.Combine(temp.FullName, "model.json");
        await File.WriteAllTextAsync(validPath, """
        {
          "model_type": "logreg_numpy",
          "feature_names": [
            "bm25_norm",
            "semantic_norm",
            "hybrid_score",
            "query_caption_overlap",
            "query_transcript_overlap",
            "query_ocr_overlap",
            "segment_duration_sec",
            "rank_inverse"
          ],
          "weights": [0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8],
          "bias": -1.0,
          "feature_mean": [0,0,0,0,0,0,0,0],
          "feature_std": [1,1,1,1,1,1,1,1],
          "training_stats": { "samples": 8 }
        }
        """);
        var invalidPath = Path.Combine(temp.FullName, "invalid.json");
        await File.WriteAllTextAsync(invalidPath, """
        {
          "model_type": "logreg_numpy",
          "feature_names": ["wrong"],
          "weights": [0],
          "bias": 0,
          "feature_mean": [0],
          "feature_std": [1]
        }
        """);

        var registry = new RerankerRegistry();
        var info = await registry.ValidateAsync("fixture", validPath);
        Assert.Equal("logreg_numpy", info.ModelType);
        Assert.Equal(8, info.FeatureNames.Count);
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.ValidateAsync("bad", invalidPath));

        var adapter = Path.Combine(root, "web", "bridge", "reranker_adapter.py");
        var ok = await RunPythonAsync(adapter, validPath);
        Assert.Equal(0, ok.ExitCode);
        Assert.Contains("\"status\":\"ok\"", ok.Output);
        var bad = await RunPythonAsync(adapter, invalidPath);
        Assert.Equal(2, bad.ExitCode);
        Assert.Contains("error", bad.Error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunPythonAsync(string adapter, string modelPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "python",
            ArgumentList = { adapter, "--model", modelPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Python did not start.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
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

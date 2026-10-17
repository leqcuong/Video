using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Research.Benchmarks;
using ThesisDemo.Infrastructure.Research;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.UnitTests;

public sealed class BenchmarkTransformTests
{
    [Fact]
    public void Sanitize_keeps_raw_source_and_diagnoses_removals()
    {
        var source = """
            [
              {"query":"Video opening", "expected":[{"asset_id":"a","start_sec":0,"end_sec":2}]},
              {"query":"video   OPENING!", "expected":[{"asset_id":"a","start_sec":0,"end_sec":2}]},
              {"query":"", "expected":[]},
              {"query":"Ending", "expected":[{"asset_id":"a","start_sec":3,"end_sec":2}]}
            ]
            """;
        var clean = BenchmarkTransforms.Sanitize(source);
        Assert.Equal(4, clean.InputCount);
        Assert.Equal(1, clean.RetainedCount);
        Assert.Equal(1, clean.Removed["duplicate_query"]);
        Assert.Equal(1, clean.Removed["missing_query"]);
        Assert.Equal(1, clean.Removed["invalid_span"]);
        Assert.Contains("video   OPENING!", source);
    }

    [Fact]
    public void Split_is_deterministic_and_has_no_query_overlap()
    {
        var rows = Enumerable.Range(0, 12).Select(i => new
        {
            query = "event " + i,
            expected = new[] { new { asset_id = "a", start_sec = 0, end_sec = 2 } }
        }).ToArray();
        var json = JsonSerializer.Serialize(rows);
        var first = BenchmarkTransforms.Split(json, .7, 42);
        var again = BenchmarkTransforms.Split(json, .7, 42);
        Assert.Equal(first.TrainingJson, again.TrainingJson);
        Assert.Equal(first.EvaluationJson, again.EvaluationJson);
        Assert.Equal(0, first.SharedNormalizedQueries);
        Assert.Equal(12, first.TrainingCount + first.EvaluationCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkTransforms.Split(json, 1.0, 42));
    }

    [Fact]
    public void Sanitize_accepts_legacy_single_span_and_unicode_queries()
    {
        var rows = """
            [{"query":"Mở cửa hàng", "asset_id":"a", "start_sec":0, "end_sec":2},
             {"query":"mở   CỬA HÀNG!", "expected":[{"asset_id":"b", "start_sec":1,"end_sec":3}]}]
            """;
        var clean = BenchmarkTransforms.Sanitize(rows);
        Assert.Equal(1, clean.RetainedCount);
        Assert.Equal(1, clean.Removed["duplicate_query"]);
    }

    [Fact]
    public async Task Raw_benchmark_preserves_bytes_and_detects_mutation()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"benchmarks-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Storage:ManagedRoot"] = folder }).Build();
            var store = new BenchmarkStore(new ManagedStorage(configuration));
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("  [{\"query\":\"test\",\"asset_id\":\"a\",\"start_sec\":0,\"end_sec\":1}] \n")];
            var version = await store.ImportAsync("source", new MemoryStream(bytes), default);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), version.Sha256);
            var saved = Path.Combine(folder, "benchmarks", version.Id.ToString("N"), "rows.json");
            Assert.Equal(bytes, await File.ReadAllBytesAsync(saved));
            var child = await store.SanitizeAsync(version.Id, default);
            Assert.Equal(1, child.RowCount);
            await File.AppendAllTextAsync(saved, " ");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(version.Id, default));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}

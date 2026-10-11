using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;
using ThesisDemo.Application.Research.Exports;

namespace ThesisDemo.Infrastructure.Research;

public sealed class ReportImporter(IDbContextFactory<ThesisDemoDbContext> factory, LocalMediaStore storage, ManagedStorage managed)
{
    private const string SourceId = "full-rerun";
    private const string Version = "full_rerun_wave100_v1";
    private static readonly string[] Names = ["segmentation_report", "caption_quality_report", "report", "reranker_comparison_report", "retrieval_significance"];
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<RecordedEvaluation>> EnsureImportedAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var source = storage.GetSource(SourceId);
            var paths = Names.ToDictionary(n => n, n => Path.Combine(source.ArtifactRoot, "eval", $"{n}_{Version}.json"));
            Dictionary<string, (string Expected, string Retrieved)>? baselineEvidence = null;
            if (File.Exists(paths["report"]))
            {
                using var baselineFile = JsonDocument.Parse(await File.ReadAllBytesAsync(paths["report"], ct));
                baselineEvidence = baselineFile.RootElement.GetProperty("per_query").EnumerateArray()
                    .ToDictionary(x => x.GetProperty("query").GetString()!, x =>
                        (x.GetProperty("expected_spans").GetRawText(), SanitizeTopResult(x)), StringComparer.Ordinal);
            }
            foreach (var (kind, path) in paths)
            {
                if (!File.Exists(path)) continue;
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var key = $"{SourceId}:{kind}:{Version}";
                var prior = await db.RecordedEvaluations.SingleOrDefaultAsync(x => x.SourceId == key, ct);
                if (prior is not null)
                {
                    if (prior.SourceHash != hash) throw new InvalidDataException("Registered evidence changed after import.");
                    continue;
                }
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                var summary = BuildSummary(kind, root);
                var provenance = JsonSerializer.Serialize(new
                {
                    sourceId = SourceId, sourceVersion = Version, sha256 = hash, recorded = true,
                    inputAssetCount = (int?)null, labelProvenance = "historical/unknown; not independent human ground truth",
                    note = "Reports describe 3486 cleaned queries; original benchmark had 3500 rows. Current corpus may contain 895 assets; historical thesis cited 896. These are distinct observations."
                });
                var evaluation = new RecordedEvaluation
                {
                    SourceId = key, Kind = kind, SourceHash = hash,
                    SummaryJson = JsonSerializer.Serialize(summary), ProvenanceJson = provenance
                };
                db.RecordedEvaluations.Add(evaluation);
                foreach (var extension in new[] { "json", "csv", "md" })
                {
                    var data = SummaryExporter.Render(kind, evaluation.SummaryJson, extension);
                    var record = await managed.WriteAsync("recorded-summary-" + extension, evaluation.Id, extension, data,
                        extension == "json" ? "application/json" : extension == "csv" ? "text/csv" : "text/markdown", provenance, ct);
                    db.Artifacts.Add(record);
                }
                if (kind is "report" or "reranker_comparison_report")
                {
                    var ordinal = 0;
                    var property = kind == "report" ? "per_query" : "per_query_delta";
                    foreach (var query in root.GetProperty(property).EnumerateArray())
                    {
                        var text = query.GetProperty("query").GetString() ?? "";
                        var rank = NullableRank(query, kind == "report" ? "first_hit_rank" : "baseline_first_hit_rank");
                        var rerankedRank = kind == "report" ? null : NullableRank(query, "reranked_first_hit_rank");
                        var outcome = kind == "report" ? (rank.HasValue ? "success" : "failure") :
                            !rank.HasValue && rerankedRank.HasValue ? "improved" :
                            rank.HasValue && !rerankedRank.HasValue ? "worsened" :
                            rank.HasValue && rerankedRank.HasValue && rerankedRank < rank ? "improved" :
                            rank.HasValue && rerankedRank.HasValue && rerankedRank > rank ? "worsened" : "unchanged";
                        var evidence = baselineEvidence is not null && baselineEvidence.TryGetValue(text, out var matched)
                            ? matched : (Expected: "[]", Retrieved: "{}");
                        db.RecordedQueries.Add(new RecordedQuery
                        {
                            EvaluationId = evaluation.Id, Ordinal = ++ordinal,
                            Query = text, Outcome = outcome, BaselineRank = rank, RerankedRank = rerankedRank,
                            BaselineTop1Iou = Number(query, kind == "report" ? "top1_iou" : "baseline_top1_iou"),
                            RerankedTop1Iou = Number(query, "reranked_top1_iou"),
                            ExpectedJson = kind == "report" ? query.GetProperty("expected_spans").GetRawText() : evidence.Expected,
                            RetrievedJson = kind == "report" ? SanitizeTopResult(query) : evidence.Retrieved
                        });
                    }
                }
                await db.SaveChangesAsync(ct);
            }
            return await db.RecordedEvaluations.AsNoTracking().OrderBy(x => x.Kind).ToListAsync(ct);
        }
        finally { Gate.Release(); }
    }

    private static object BuildSummary(string kind, JsonElement root)
    {
        var selected = kind switch
        {
            "segmentation_report" => new[] { "events_total", "events_matched", "boundary_hit_rate", "mean_iou", "tolerance_sec" },
            "caption_quality_report" => new[] { "samples_total_labels", "samples_evaluated", "mean_correctness", "mean_specificity", "mean_grounding", "low_information_rate", "category_breakdown" },
            "report" => new[] { "queries_evaluated", "top_k", "iou_threshold", "recall_at_k", "mrr", "mean_top1_iou" },
            "reranker_comparison_report" => new[] { "top_k", "candidate_k", "hybrid_alpha", "iou_threshold", "deltas" },
            _ => new[] { "queries_evaluated", "bootstrap_iterations", "metrics" }
        };
        var dictionary = new Dictionary<string, JsonElement>();
        foreach (var name in selected)
            if (root.TryGetProperty(name, out var element)) dictionary[name] = element.Clone();
        if (kind == "reranker_comparison_report")
            foreach (var method in new[] { "baseline", "reranked" })
                if (root.TryGetProperty(method, out var node))
                    dictionary[method] = JsonSerializer.SerializeToElement(new
                    {
                        queries_evaluated = Number(node, "queries_evaluated"),
                        recall_at_k = Number(node, "recall_at_k"), mrr = Number(node, "mrr"), mean_top1_iou = Number(node, "mean_top1_iou")
                    });
        return dictionary;
    }

    private static string SanitizeTopResult(JsonElement query)
    {
        if (!query.TryGetProperty("top_result", out var top) || top.ValueKind != JsonValueKind.Object) return "{}";
        return JsonSerializer.Serialize(new
        {
            assetId = String(top, "asset_id"), segmentId = String(top, "segment_id"),
            startSec = Number(top, "start_sec"), endSec = Number(top, "end_sec"), caption = String(top, "caption")
        });
    }
    private static string? String(JsonElement root, string key) => root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    private static double Number(JsonElement root, string key) => root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Number ? node.GetDouble() : 0;
    private static int? NullableRank(JsonElement root, string key) => root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Number ? node.GetInt32() : null;
}

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Search;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Infrastructure.Legacy;

public sealed class LegacySearchService(
    IDbContextFactory<ThesisDemoDbContext> factory,
    LocalMediaStore mediaStore,
    IConfiguration configuration) : ISearchService
{
    public async Task<SearchResponse> SearchAsync(Guid collectionId, SearchRequest request, CancellationToken ct)
    {
        SearchValidation.Validate(request);
        var started = Stopwatch.StartNew();
        await using var db = await factory.CreateDbContextAsync(ct);
        var collectionExists = await db.Collections.AsNoTracking().AnyAsync(x => x.Id == collectionId, ct);
        if (!collectionExists) throw new KeyNotFoundException("Collection was not found.");

        var assetFilter = request.AssetIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (assetFilter.Length > 0)
        {
            var scoped = await db.VideoAssets.AsNoTracking()
                .CountAsync(x => x.CollectionId == collectionId && assetFilter.Contains(x.Id), ct);
            if (scoped != assetFilter.Length) throw new InvalidOperationException("Asset filter contains assets outside the collection.");
        }

        var source = mediaStore.GetSource(configuration["Legacy:SearchSourceId"] ?? mediaStore.ListSources().First().Id);
        var indexPath = Path.Combine(source.ArtifactRoot, "index", "combined_index.json");
        if (!File.Exists(indexPath)) throw new FileNotFoundException("Registered legacy search index was not found.", indexPath);

        var legacyAssetScope = assetFilter.Length == 0
            ? []
            : await db.VideoAssets.AsNoTracking()
                .Where(x => assetFilter.Contains(x.Id))
                .Select(x => x.LegacyAssetId)
                .ToArrayAsync(ct);

        var workerOptions = new PythonSearchProcessOptions(
            PythonExecutable: configuration["Legacy:PythonExecutable"] ?? ResolvePython(),
            WorkerScriptPath: Path.Combine(FindRepositoryRoot(), "web", "bridge", "search_worker.py"),
            WorkingDirectory: FindRepositoryRoot(),
            RequestTimeout: TimeSpan.FromSeconds(configuration.GetValue("Search:RequestTimeoutSeconds", 15)),
            RestartTimeout: TimeSpan.FromSeconds(5));
        await using var process = new PythonSearchProcess(workerOptions);
        await process.StartAsync(ct);
        await process.LoadIndexAsync(request.IndexId?.ToString("D") ?? "legacy-current", indexPath, ct);
        await process.WarmAsync(ct);
        var response = await process.SearchAsync(request.IndexId?.ToString("D") ?? "legacy-current",
            request.Query.Trim(), request.TopK, request.CandidateK, request.HybridAlpha, legacyAssetScope, ct);

        var baseline = await MapResultsAsync(db, collectionId, response.BaselineResults ?? [], baselineRanks: null, ct);
        IReadOnlyList<SearchResultDto>? reranked = null;
        if (request.Mode == SearchMode.Compare)
        {
            var ranks = baseline.ToDictionary(x => (x.LegacyAssetId, x.LegacySegmentId), x => x.Rank);
            reranked = baseline.Select(x => x with { ScoreKind = "reranker", BaselineRank = ranks[(x.LegacyAssetId, x.LegacySegmentId)] }).ToList();
        }
        else if (request.Mode == SearchMode.Reranked)
        {
            reranked = baseline.Select(x => x with { ScoreKind = "reranker" }).ToList();
            baseline = null;
        }

        started.Stop();
        return new SearchResponse(response.RequestId, response.IndexId, request.ModelId,
            new SearchEffectiveConfigDto(response.EffectiveConfig?.TopK ?? request.TopK,
                response.EffectiveConfig?.CandidateK ?? request.CandidateK,
                response.EffectiveConfig?.HybridAlpha ?? request.HybridAlpha),
            response.ElapsedMs ?? started.ElapsedMilliseconds,
            response.Warnings ?? [],
            baseline,
            reranked);
    }

    private static async Task<IReadOnlyList<SearchResultDto>> MapResultsAsync(ThesisDemoDbContext db, Guid collectionId,
        IReadOnlyList<SearchWorkerResult> rows, IReadOnlyDictionary<(string AssetId, string SegmentId), int>? baselineRanks, CancellationToken ct)
    {
        var legacyAssetIds = rows.Select(x => x.AssetId).Distinct().ToArray();
        var assets = await db.VideoAssets.AsNoTracking()
            .Where(x => x.CollectionId == collectionId && legacyAssetIds.Contains(x.LegacyAssetId))
            .ToDictionaryAsync(x => x.LegacyAssetId, ct);
        var assetIds = assets.Values.Select(x => x.Id).ToArray();
        var segmentSets = await db.SegmentSets.AsNoTracking()
            .Where(x => assetIds.Contains(x.AssetId))
            .Select(x => new { x.Id, x.AssetId, x.CreatedUtc })
            .ToListAsync(ct);
        var latestSetByAsset = segmentSets.GroupBy(x => x.AssetId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.CreatedUtc).First().Id);
        var setIds = latestSetByAsset.Values.ToArray();
        var segments = await db.VideoSegments.AsNoTracking()
            .Where(x => setIds.Contains(x.SegmentSetId))
            .ToListAsync(ct);
        var segmentsBySetAndLegacy = segments.ToDictionary(x => (x.SegmentSetId, x.LegacySegmentId));
        var mapped = new List<SearchResultDto>();
        var rank = 1;
        foreach (var row in rows)
        {
            if (!assets.TryGetValue(row.AssetId, out var asset)) continue;
            if (!latestSetByAsset.TryGetValue(asset.Id, out var setId)) continue;
            if (!segmentsBySetAndLegacy.TryGetValue((setId, row.SegmentId), out var segment)) continue;
            mapped.Add(new SearchResultDto(rank++, segment.Id, asset.Id, asset.LegacyAssetId, segment.LegacySegmentId,
                segment.StartSec, segment.EndSec, segment.Caption, segment.TranscriptText, segment.OcrText,
                row.Score, row.ScoreKind, $"/api/v1/assets/{asset.Id}/media",
                segment.ThumbnailArtifactId.HasValue ? $"/api/v1/segments/{segment.Id}/thumbnail" : null,
                baselineRanks?.GetValueOrDefault((asset.LegacyAssetId, segment.LegacySegmentId))));
        }
        return mapped;
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
                process.WaitForExit(3_000);
                if (process.ExitCode == 0) return candidate;
            }
            catch { }
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
        return Directory.GetCurrentDirectory();
    }
}

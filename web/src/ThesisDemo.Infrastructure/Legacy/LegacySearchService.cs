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
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == collectionId, ct)
            ?? throw new KeyNotFoundException("Collection was not found.");
        if (request.Mode != SearchMode.Baseline)
            throw new InvalidOperationException("Live reranking is not available. Recorded Track C comparisons are available in Research.");

        var assetFilter = request.AssetIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (assetFilter.Length > 0)
        {
            var scoped = await db.VideoAssets.AsNoTracking()
                .CountAsync(x => x.CollectionId == collectionId && assetFilter.Contains(x.Id), ct);
            if (scoped != assetFilter.Length) throw new InvalidOperationException("Asset filter contains assets outside the collection.");
        }

        var selectedId = request.IndexId ?? collection.ActiveIndexId;
        Dictionary<Guid, Guid>? memberSets = null;
        string indexPath;
        if (selectedId.HasValue)
        {
            var snapshot = await db.IndexSnapshots.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == selectedId.Value && x.CollectionId == collectionId, ct)
                ?? throw new InvalidOperationException("Selected index is not part of this collection.");
            if (snapshot.State != ThesisDemo.Domain.Search.SnapshotState.Ready || !snapshot.ArtifactId.HasValue)
                throw new InvalidOperationException("Selected index is not ready.");
            var artifact = await db.Artifacts.AsNoTracking().SingleAsync(x => x.Id == snapshot.ArtifactId.Value, ct);
            indexPath = artifact.RelativePath.StartsWith("managed/", StringComparison.Ordinal)
                ? new ManagedStorage(configuration).Resolve(artifact.RelativePath)
                : throw new InvalidOperationException("Selected index artifact is not registered in managed storage.");
            memberSets = await db.IndexMembers.AsNoTracking().Where(x => x.IndexId == selectedId.Value)
                .ToDictionaryAsync(x => x.AssetId, x => x.SegmentSetId, ct);
        }
        else
        {
            var source = mediaStore.GetSource(configuration["Legacy:SearchSourceId"] ?? mediaStore.ListSources().First().Id);
            indexPath = Path.Combine(source.ArtifactRoot, "index", "combined_index.json");
        }
        if (!File.Exists(indexPath)) throw new FileNotFoundException("Registered legacy search index was not found.", indexPath);

        var legacyAssetScope = assetFilter.Length == 0
            ? []
            : await db.VideoAssets.AsNoTracking()
                .Where(x => assetFilter.Contains(x.Id))
                .Select(x => new { x.LegacyAssetId, x.Id })
                .ToArrayAsync(ct);
        var workerScope = legacyAssetScope.SelectMany(x => new[] { x.LegacyAssetId, x.Id.ToString("N") }).ToArray();
        var workerIndexId = selectedId?.ToString("D") ?? "legacy-current";

        var workerOptions = new PythonSearchProcessOptions(
            PythonExecutable: configuration["Legacy:PythonExecutable"] ?? ResolvePython(),
            WorkerScriptPath: Path.Combine(FindRepositoryRoot(), "web", "bridge", "search_worker.py"),
            WorkingDirectory: FindRepositoryRoot(),
            RequestTimeout: TimeSpan.FromSeconds(configuration.GetValue("Search:RequestTimeoutSeconds", 15)),
            RestartTimeout: TimeSpan.FromSeconds(5));
        await using var process = new PythonSearchProcess(workerOptions);
        await process.StartAsync(ct);
        await process.LoadIndexAsync(workerIndexId, indexPath, ct);
        await process.WarmAsync(ct);
        var response = await process.SearchAsync(workerIndexId,
            request.Query.Trim(), request.TopK, request.CandidateK, request.HybridAlpha, workerScope, ct);

        var baseline = await MapResultsAsync(db, collectionId, response.BaselineResults ?? [], memberSets, ct);

        started.Stop();
        return new SearchResponse(response.RequestId, response.IndexId, request.ModelId,
            new SearchEffectiveConfigDto(response.EffectiveConfig?.TopK ?? request.TopK,
                response.EffectiveConfig?.CandidateK ?? request.CandidateK,
                response.EffectiveConfig?.HybridAlpha ?? request.HybridAlpha),
            response.ElapsedMs ?? started.ElapsedMilliseconds,
            response.Warnings ?? [],
            baseline,
            null);
    }

    private static async Task<IReadOnlyList<SearchResultDto>> MapResultsAsync(ThesisDemoDbContext db, Guid collectionId,
        IReadOnlyList<SearchWorkerResult> rows, IReadOnlyDictionary<Guid, Guid>? memberSets, CancellationToken ct)
    {
        var legacyAssetIds = rows.Select(x => x.AssetId).Distinct().ToArray();
        var assets = await db.VideoAssets.AsNoTracking()
            .Where(x => x.CollectionId == collectionId)
            .ToListAsync(ct);
        var byWorkerId = assets.SelectMany(x => new[] { (Key: x.LegacyAssetId, Asset: x), (Key: x.Id.ToString("N"), Asset: x) })
            .GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().Asset, StringComparer.Ordinal);
        var matched = legacyAssetIds.Where(byWorkerId.ContainsKey).Select(x => byWorkerId[x]).DistinctBy(x => x.Id).ToArray();
        var assetIds = matched.Select(x => x.Id).ToArray();
        var segmentSets = await db.SegmentSets.AsNoTracking()
            .Where(x => assetIds.Contains(x.AssetId))
            .Select(x => new { x.Id, x.AssetId, x.CreatedUtc })
            .ToListAsync(ct);
        var latestSetByAsset = memberSets is null
            ? segmentSets.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.CreatedUtc).First().Id)
            : memberSets.Where(x => assetIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        var setIds = latestSetByAsset.Values.ToArray();
        var segments = await db.VideoSegments.AsNoTracking()
            .Where(x => setIds.Contains(x.SegmentSetId))
            .ToListAsync(ct);
        var segmentsBySetAndLegacy = segments.ToDictionary(x => (x.SegmentSetId, x.LegacySegmentId));
        var mapped = new List<SearchResultDto>();
        var rank = 1;
        foreach (var row in rows)
        {
            if (!byWorkerId.TryGetValue(row.AssetId, out var asset)) continue;
            if (!latestSetByAsset.TryGetValue(asset.Id, out var setId)) continue;
            if (!segmentsBySetAndLegacy.TryGetValue((setId, row.SegmentId), out var segment)) continue;
            mapped.Add(new SearchResultDto(rank++, segment.Id, asset.Id, asset.LegacyAssetId, segment.LegacySegmentId,
                segment.StartSec, segment.EndSec, segment.Caption, segment.TranscriptText, segment.OcrText,
                row.Score, row.ScoreKind, $"/api/v1/assets/{asset.Id}/media",
                segment.ThumbnailArtifactId.HasValue ? $"/api/v1/segments/{segment.Id}/thumbnail" : null,
                null));
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

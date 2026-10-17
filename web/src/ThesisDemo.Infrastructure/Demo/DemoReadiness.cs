using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Demo;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Search;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Infrastructure.Demo;

public sealed class DemoReadiness(IDbContextFactory<ThesisDemoDbContext> factory,
    ManagedStorage managed, LocalMediaStore legacy)
{
    public async Task<DemoPreflight> CheckAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var collections = await db.Collections.AsNoTracking().ToListAsync(ct);
        var snapshots = await db.IndexSnapshots.AsNoTracking().ToListAsync(ct);
        var assets = await db.VideoAssets.AsNoTracking().ToListAsync(ct);
        var mediaIds = assets.Where(x => x.MediaArtifactId.HasValue)
            .Select(x => x.MediaArtifactId!.Value).Distinct().ToArray();
        var media = await db.Artifacts.AsNoTracking().Where(x => mediaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.RelativePath, ct);
        var missing = 0;
        var unknown = 0;
        foreach (var asset in assets)
        {
            if (asset.MediaArtifactId is not Guid mediaId || !media.TryGetValue(mediaId, out var path))
            { missing++; continue; }
            try
            {
                var file = path.StartsWith("managed/", StringComparison.Ordinal)
                    ? managed.Resolve(path) : legacy.ResolveRegisteredArtifactPath(path);
                if (!File.Exists(file)) missing++;
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or KeyNotFoundException)
            { unknown++; }
        }
        var active = collections.Select(x => snapshots.FirstOrDefault(s => s.Id == x.ActiveIndexId))
            .Where(x => x is { State: SnapshotState.Ready, ArtifactId: not null }).ToArray();
        var checks = new List<DemoCheck>
        {
            new("Database", "ready", $"{collections.Count} collections; {assets.Count} registered video assets."),
            new("Active index", active.Length > 0 ? "ready" : "blocked",
                $"{active.Length} collection(s) with a ready active snapshot. Index/model compatibility at runtime not yet measured."),
            new("Media files", missing == 0 && unknown == 0 && assets.Count > 0 ? "ready" : "blocked",
                $"{assets.Count} assets; {missing} missing/unattached media; {unknown} unresolvable paths. File existence only, not playback verification."),
            new("Model runtime", "unverified", "Embedding/reranker model load, GPU and inference are not tested by this preflight."),
            new("Colab handoff", "unverified", "Requires a real external GPU receipt and verified import before release."),
            new("Offline rehearsal", "unverified", "Measure 20 warm searches, memory, media seeking, responsiveness and restart offline on this laptop.")
        };
        var reports = await db.RecordedEvaluations.AsNoTracking().CountAsync(ct);
        checks.Add(new DemoCheck("Recorded research", reports > 0 ? "ready" : "blocked",
            $"{reports} imported report(s); historical labels are not independent human ground truth."));
        var queries = await db.RecordedQueries.AsNoTracking().ToListAsync(ct);
        var examples = new List<DemoQuery>();
        AddExample("Search hit", queries.FirstOrDefault(x => x.Outcome == "success"), examples);
        AddExample("Rank change", queries.FirstOrDefault(x => x.Outcome == "improved"), examples);
        AddExample("Search failure", queries.FirstOrDefault(x => x.Outcome == "failure"), examples);
        return new DemoPreflight(checks, examples, DateTimeOffset.UtcNow);
    }

    private static void AddExample(string purpose, Infrastructure.Research.RecordedQuery? query, List<DemoQuery> result)
    {
        if (query is not null)
            result.Add(new DemoQuery(purpose, query.Query,
                $"Recorded: {query.Outcome}; baseline rank {query.BaselineRank?.ToString() ?? "none"}; reranked rank {query.RerankedRank?.ToString() ?? "none"}. Playback not verified."));
    }
}

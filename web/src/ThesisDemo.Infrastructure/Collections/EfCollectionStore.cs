using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Collections;
using ThesisDemo.Domain.Library;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Collections;

public sealed class EfCollectionStore(IDbContextFactory<ThesisDemoDbContext> factory) : ICollectionStore
{
    public async Task<PageResult<CollectionDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Collections.AsNoTracking().OrderBy(x => x.Name);
        var total = await query.CountAsync(ct);
        var collections = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = collections.Select(x => x.Id).ToArray();
        var counts = await db.VideoAssets.AsNoTracking()
            .Where(x => ids.Contains(x.CollectionId))
            .GroupBy(x => x.CollectionId)
            .Select(x => new { CollectionId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.CollectionId, x => x.Count, ct);
        return new PageResult<CollectionDto>(collections.Select(x => ToDto(x, counts.GetValueOrDefault(x.Id))).ToList(), page, pageSize, total);
    }

    public async Task<CollectionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (collection is null) return null;
        var count = await db.VideoAssets.AsNoTracking().CountAsync(x => x.CollectionId == id, ct);
        return ToDto(collection, count);
    }

    public async Task<CollectionDto> CreateAsync(CreateCollectionCommand command, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var collection = new Collection(command.Name);
        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);
        return ToDto(collection, 0);
    }

    public async Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionCommand command, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var collection = await db.Collections.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Collection was not found.");
        if (!string.IsNullOrWhiteSpace(command.IfMatch) && !VersionMatches(collection.Version, command.IfMatch))
            throw new DbUpdateConcurrencyException("The collection version is stale.");
        if (!string.IsNullOrWhiteSpace(command.Name)) collection.Rename(command.Name);
        var count = await db.VideoAssets.CountAsync(x => x.CollectionId == id, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(collection, count);
    }

    public async Task<PageResult<AssetDto>> ListAssetsAsync(Guid collectionId, ListAssetsQuery query, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Collections.AnyAsync(x => x.Id == collectionId, ct)) throw new KeyNotFoundException("Collection was not found.");
        var assets = db.VideoAssets.AsNoTracking().Where(x => x.CollectionId == collectionId);
        if (query.State.HasValue) assets = assets.Where(x => x.State == query.State.Value);
        if (!string.IsNullOrWhiteSpace(query.Name)) assets = assets.Where(x => x.OriginalFileName.Contains(query.Name) || x.LegacyAssetId.Contains(query.Name));
        assets = assets.OrderBy(x => x.OriginalFileName).ThenBy(x => x.LegacyAssetId);
        var total = await assets.CountAsync(ct);
        var pageItems = await assets.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var assetIds = pageItems.Select(x => x.Id).ToArray();
        var segmentRows = await db.SegmentSets.AsNoTracking()
            .Where(x => assetIds.Contains(x.AssetId))
            .Select(x => new { x.AssetId, x.Id, x.CreatedUtc })
            .ToListAsync(ct);
        var segmentSets = segmentRows.GroupBy(x => x.AssetId)
            .ToDictionary(x => x.Key, x => (Guid?)x.OrderByDescending(s => s.CreatedUtc).First().Id);
        return new PageResult<AssetDto>(pageItems.Select(x => ToDto(x, segmentSets.GetValueOrDefault(x.Id))).ToList(), query.Page, query.PageSize, total);
    }

    private static bool VersionMatches(Guid version, string etag) =>
        Guid.TryParse(etag.Trim().Trim('"'), out var parsed) && parsed == version;

    private static CollectionDto ToDto(Collection collection, int assetCount) =>
        new(collection.Id, collection.Name, false, collection.ActiveIndexId, null, assetCount, collection.CreatedUtc, collection.Version.ToString("D"));

    private static AssetDto ToDto(VideoAsset asset, Guid? segmentSetId) =>
        new(asset.Id, asset.CollectionId, asset.OriginalFileName, asset.LegacyAssetId, asset.State.ToString(),
            asset.MediaArtifactId.HasValue ? "available" : "missing", asset.DurationSec, asset.Fps,
            asset.Width, asset.Height, null, segmentSetId);
}

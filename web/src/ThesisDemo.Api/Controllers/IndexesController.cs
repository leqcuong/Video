using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Domain.Search;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/collections/{collectionId:guid}")]
public sealed class IndexesController(IDbContextFactory<ThesisDemoDbContext> factory) : ControllerBase
{
    [HttpGet("indexes")]
    public async Task<IActionResult> List(Guid collectionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest();
        await using var db = await factory.CreateDbContextAsync(ct);
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == collectionId, ct);
        if (collection is null) return NotFound();
        Response.Headers.ETag = $"\"{collection.Version:D}\"";
        var query = db.IndexSnapshots.AsNoTracking().Where(x => x.CollectionId == collectionId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize, collection.ActiveIndexId });
    }

    public sealed record ActivateRequest(Guid IndexId);

    [HttpPut("active-index")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid collectionId, [FromBody] ActivateRequest request, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var header)) return StatusCode(428, new { code = "PRECONDITION_REQUIRED" });
        if (!Guid.TryParse(header.ToString().Trim('"'), out var expected)) return BadRequest(new { code = "INVALID_ETAG" });
        await using var db = await factory.CreateDbContextAsync(ct);
        var collection = await db.Collections.SingleOrDefaultAsync(x => x.Id == collectionId, ct);
        if (collection is null) return NotFound();
        if (collection.Version != expected) return StatusCode(412, new { code = "STALE_ETAG" });
        var snapshot = await db.IndexSnapshots.SingleOrDefaultAsync(x => x.Id == request.IndexId && x.CollectionId == collectionId, ct);
        if (snapshot is null) return NotFound();
        if (snapshot.State != SnapshotState.Ready) return Conflict(new { code = "INDEX_NOT_READY" });
        collection.ActivateIndex(snapshot);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return StatusCode(412, new { code = "STALE_ETAG" }); }
        Response.Headers.ETag = $"\"{collection.Version:D}\"";
        return Ok(new { collection.Id, collection.ActiveIndexId, collection.Version });
    }
}

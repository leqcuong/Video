using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Collections;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
public sealed class SegmentsController(IDbContextFactory<ThesisDemoDbContext> factory) : ControllerBase
{
    [HttpGet("{id:guid}/segments")]
    public async Task<ActionResult<PageResult<SegmentDto>>> List(Guid id, [FromQuery] Guid? indexId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.VideoAssets.AnyAsync(x => x.Id == id, ct))
            return NotFoundProblem("ASSET_NOT_FOUND", "Asset was not found.");

        Guid? segmentSetId;
        if (indexId.HasValue)
        {
            segmentSetId = await db.IndexMembers.AsNoTracking()
                .Where(x => x.IndexId == indexId.Value && x.AssetId == id)
                .Select(x => (Guid?)x.SegmentSetId)
                .SingleOrDefaultAsync(ct);
            if (segmentSetId is null) return NotFoundProblem("INDEX_MEMBER_NOT_FOUND", "Asset is not part of the selected index.");
        }
        else
        {
            var segmentSets = await db.SegmentSets.AsNoTracking()
                .Where(x => x.AssetId == id)
                .Select(x => new { x.Id, x.CreatedUtc })
                .ToListAsync(ct);
            segmentSetId = segmentSets.OrderByDescending(x => x.CreatedUtc).Select(x => (Guid?)x.Id).FirstOrDefault();
            if (segmentSetId is null) return Ok(new PageResult<SegmentDto>([], page, pageSize, 0));
        }

        var query = db.VideoSegments.AsNoTracking()
            .Where(x => x.SegmentSetId == segmentSetId.Value)
            .OrderBy(x => x.StartSec)
            .ThenBy(x => x.LegacySegmentId);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new SegmentDto(x.Id, id, x.SegmentSetId, x.LegacySegmentId,
            x.StartSec, x.EndSec, x.Caption, x.TranscriptText, x.OcrText,
            ModalityAvailability(x.TranscriptText, x.OcrText), null, Url.Action("Media", "Media", new { id }))).ToList();
        return Ok(new PageResult<SegmentDto>(items, page, pageSize, total));
    }

    private ActionResult NotFoundProblem(string code, string detail) =>
        Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found", extensions: ProblemExtensions(code));

    private Dictionary<string, object?> ProblemExtensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = HttpContext.TraceIdentifier
    };

    private static string ModalityAvailability(string? transcript, string? ocr)
    {
        var values = new List<string> { "caption" };
        if (!string.IsNullOrWhiteSpace(transcript)) values.Add("transcript");
        if (!string.IsNullOrWhiteSpace(ocr)) values.Add("ocr");
        return string.Join(",", values);
    }
}

[ApiController]
[Route("api/v1/segments")]
public sealed class SegmentThumbnailsController(IDbContextFactory<ThesisDemoDbContext> factory, LocalMediaStore mediaStore) : ControllerBase
{
    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var segment = await db.VideoSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (segment is null) return NotFound(new { code = "SEGMENT_NOT_FOUND", traceId = HttpContext.TraceIdentifier });
        if (segment.ThumbnailArtifactId is null) return NotFound(new { code = "THUMBNAIL_NOT_AVAILABLE", traceId = HttpContext.TraceIdentifier });
        var artifact = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == segment.ThumbnailArtifactId, ct);
        if (artifact is null) return NotFound(new { code = "THUMBNAIL_NOT_AVAILABLE", traceId = HttpContext.TraceIdentifier });
        string path;
        try
        {
            path = mediaStore.ResolveRegisteredArtifactPath(artifact.RelativePath);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Invalid thumbnail artifact", extensions: ProblemExtensions("INVALID_THUMBNAIL_ARTIFACT"));
        }
        if (!System.IO.File.Exists(path)) return NotFound(new { code = "THUMBNAIL_FILE_MISSING", traceId = HttpContext.TraceIdentifier });
        return PhysicalFile(path, artifact.ContentType, enableRangeProcessing: false);
    }

    private Dictionary<string, object?> ProblemExtensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = HttpContext.TraceIdentifier
    };
}

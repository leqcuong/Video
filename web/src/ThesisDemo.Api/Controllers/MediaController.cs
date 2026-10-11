using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
public sealed class MediaController(IDbContextFactory<ThesisDemoDbContext> factory, LocalMediaStore mediaStore) : ControllerBase
{
    [HttpGet("{id:guid}/media")]
    [HttpHead("{id:guid}/media")]
    public async Task<IActionResult> Media(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.VideoAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null) return NotFoundProblem("ASSET_NOT_FOUND", "Asset was not found.");
        if (asset.MediaArtifactId is null) return NotFoundProblem("MEDIA_NOT_AVAILABLE", "Media is not available for this asset.");
        var artifact = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asset.MediaArtifactId, ct);
        if (artifact is null) return NotFoundProblem("MEDIA_NOT_AVAILABLE", "Media artifact is not registered.");
        string path;
        try
        {
            path = mediaStore.ResolveRegisteredArtifactPath(artifact.RelativePath);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Invalid media artifact", extensions: ProblemExtensions("INVALID_MEDIA_ARTIFACT"));
        }
        var info = new FileInfo(path);
        if (!info.Exists) return NotFoundProblem("MEDIA_FILE_MISSING", "Media file is missing.");

        Response.Headers.ETag = new EntityTagHeaderValue("\"" + artifact.Sha256 + "\"").ToString();
        Response.Headers.LastModified = info.LastWriteTimeUtc.ToString("R");
        return PhysicalFile(path, artifact.ContentType, enableRangeProcessing: true);
    }

    private ActionResult NotFoundProblem(string code, string detail) =>
        Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found", extensions: ProblemExtensions(code));

    private Dictionary<string, object?> ProblemExtensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = HttpContext.TraceIdentifier
    };
}

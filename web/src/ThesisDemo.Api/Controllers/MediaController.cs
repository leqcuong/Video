using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
public sealed class MediaController(IDbContextFactory<ThesisDemoDbContext> factory, LocalMediaStore mediaStore, ManagedStorage managed) : ControllerBase
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
            path = artifact.RelativePath.StartsWith("managed/", StringComparison.Ordinal)
                ? managed.Resolve(artifact.RelativePath) : mediaStore.ResolveRegisteredArtifactPath(artifact.RelativePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            return ProblemWithCode("INVALID_MEDIA_ARTIFACT", ex.Message, StatusCodes.Status422UnprocessableEntity, "Invalid media artifact");
        }
        var info = new FileInfo(path);
        if (!info.Exists) return NotFoundProblem("MEDIA_FILE_MISSING", "Media file is missing.");

        var etag = new EntityTagHeaderValue("\"" + artifact.Sha256 + "\"");
        return PhysicalFile(path, artifact.ContentType, lastModified: info.LastWriteTimeUtc,
            entityTag: etag, enableRangeProcessing: true);
    }

    private ActionResult NotFoundProblem(string code, string detail) =>
        ProblemWithCode(code, detail, StatusCodes.Status404NotFound, "Not found");

    private ObjectResult ProblemWithCode(string code, string detail, int statusCode, string title)
    {
        var result = Problem(detail, statusCode: statusCode, title: title);
        ((ProblemDetails)result.Value!).Extensions["code"] = code;
        return result;
    }
}

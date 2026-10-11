using Microsoft.AspNetCore.Mvc;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/collections/{collectionId:guid}/assets")]
public sealed class AssetsController(MediaUploadService uploads) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MediaUploadService.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaUploadService.MaxBytes)]
    public async Task<IActionResult> Upload(Guid collectionId, IFormFile file, CancellationToken ct)
    {
        if (file is null) return BadRequest("A video file is required.");
        try
        {
            await using var stream = file.OpenReadStream();
            var asset = await uploads.UploadAsync(collectionId, file.FileName, stream, file.Length, ct);
            return Created($"/api/v1/assets/{asset.Id}", new { asset.Id, asset.CollectionId, asset.OriginalFileName, asset.State, asset.DurationSec, asset.Fps, asset.Width, asset.Height });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UploadValidationException ex)
        {
            var code = ex.Code;
            var status = code == "UPLOAD_TOO_LARGE" ? 413 : code == "UNSUPPORTED_MEDIA" ? 415 : code == "PROBE_UNAVAILABLE" ? 503 : 422;
            var result = Problem(ex.Message, statusCode: status, title: "Video upload rejected");
            ((ProblemDetails)result.Value!).Extensions["code"] = code;
            return result;
        }
    }
}

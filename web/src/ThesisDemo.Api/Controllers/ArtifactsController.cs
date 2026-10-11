using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/artifacts")]
public sealed class ArtifactsController(IDbContextFactory<ThesisDemoDbContext> factory, ManagedStorage storage) : ControllerBase
{
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var artifact = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (artifact is null || !artifact.Kind.StartsWith("recorded-summary-", StringComparison.Ordinal)) return NotFound();
        string path;
        try { path = storage.Resolve(artifact.RelativePath); }
        catch (InvalidDataException) { return UnprocessableEntity(); }
        if (!System.IO.File.Exists(path)) return NotFound();
        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        if (bytes.LongLength != artifact.ByteLength ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(artifact.Sha256)))
            return UnprocessableEntity(new ProblemDetails { Title = "Artifact integrity verification failed", Status = 422 });
        var extension = Path.GetExtension(path);
        return File(bytes, artifact.ContentType, $"research-summary-{id:N}{extension}");
    }
}

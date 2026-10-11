using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Research;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class ResearchController(ReportImporter importer, EvaluationReadRepository reader,
    IDbContextFactory<ThesisDemoDbContext> factory) : ControllerBase
{
    [HttpGet("evaluation-runs")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var runs = await importer.EnsureImportedAsync(ct);
        return Ok(runs.Select(x => new { x.Id, x.Kind, x.SourceId, x.SourceHash, summary = System.Text.Json.JsonDocument.Parse(x.SummaryJson).RootElement.Clone(), provenance = System.Text.Json.JsonDocument.Parse(x.ProvenanceJson).RootElement.Clone() }));
    }

    [HttpGet("evaluation-runs/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await importer.EnsureImportedAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.RecordedEvaluations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound();
        var prefix = $"managed/exports/{id:N}/";
        var artifacts = await db.Artifacts.AsNoTracking().Where(x => x.RelativePath.StartsWith(prefix))
            .Select(x => new { x.Id, x.Kind, x.ContentType, x.Sha256 }).ToListAsync(ct);
        return Ok(new { item.Id, item.Kind, item.SourceId, item.SourceHash, item.SummaryJson, item.ProvenanceJson, artifacts });
    }

    [HttpGet("evaluation-runs/{id:guid}/queries")]
    public async Task<IActionResult> Queries(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? outcome = null, CancellationToken ct = default)
    {
        await importer.EnsureImportedAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.RecordedEvaluations.AnyAsync(x => x.Id == id, ct)) return NotFound();
        try { return Ok(await reader.GetQueriesAsync(id, outcome, page, pageSize, ct)); }
        catch (ArgumentException) { return BadRequest(new ProblemDetails { Title = "Invalid pagination or outcome filter", Status = 400 }); }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Processing;
using ThesisDemo.Infrastructure.Jobs;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class JobsController(ProcessingCommands commands, IDbContextFactory<ThesisDemoDbContext> factory) : ControllerBase
{
    [HttpPost("collections/{collectionId:guid}/processing-runs")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid collectionId, [FromBody] ProcessingRequest request, CancellationToken ct)
    {
        try
        {
            var job = await commands.StartAsync(collectionId, request, Request.Headers["Idempotency-Key"].FirstOrDefault(), null, ct);
            Response.Headers.Location = $"/api/v1/jobs/{job.Id}";
            return Accepted(new { job.Id, job.RunId, job.CollectionId, job.State, job.CreatedUtc });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ProcessingConflictException ex) { return Conflict(new { code = "PROCESSING_CONFLICT", message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { code = "INVALID_PROCESSING_REQUEST", message = ex.Message }); }
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> List([FromQuery] Guid? collectionId, [FromQuery] JobState? state,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest("Invalid page or pageSize.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Jobs.AsNoTracking().AsQueryable();
        if (collectionId.HasValue) query = query.Where(x => x.CollectionId == collectionId);
        if (state.HasValue) query = query.Where(x => x.State == state);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items = items.Select(Project), total, page, pageSize });
    }

    [HttpGet("jobs/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound();
        var outcomes = await db.ProcessingAssetAttempts.AsNoTracking().Where(x => x.RunId == job.RunId)
            .Select(x => new { x.AssetId, x.State, x.ErrorCode }).ToListAsync(ct);
        return Ok(new { job = Project(job), outcomes });
    }

    [HttpGet("jobs/{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, [FromQuery] long afterSequence = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 500) return BadRequest("Invalid event cursor.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound();
        var items = await db.JobEvents.AsNoTracking().Where(x => x.JobId == id && x.Id > afterSequence)
            .OrderBy(x => x.Id).Take(limit).ToListAsync(ct);
        var terminal = job.State is JobState.Succeeded or JobState.Partial or JobState.Failed or JobState.Canceled or JobState.Interrupted;
        return Ok(new { items, nextSequence = items.LastOrDefault()?.Id ?? afterSequence, isTerminal = terminal });
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var job = await commands.CancelAsync(id, ct);
            return job.State == JobState.Canceled ? Ok(Project(job)) : Accepted(Project(job));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ProcessingConflictException ex) { return Conflict(new { code = "JOB_TERMINAL", message = ex.Message }); }
    }

    public sealed record RetryRequest(bool FailedOnly = true, bool Resume = false);

    [HttpPost("jobs/{id:guid}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid id, [FromBody] RetryRequest request, CancellationToken ct)
    {
        try
        {
            var job = await commands.RetryAsync(id, request.FailedOnly, request.Resume, ct);
            Response.Headers.Location = $"/api/v1/jobs/{job.Id}";
            return Accepted(Project(job));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ProcessingConflictException ex) { return Conflict(new { code = "RETRY_CONFLICT", message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    private static object Project(DurableJob job) => new
    {
        job.Id, job.CollectionId, job.RunId, job.ParentJobId, job.Kind, job.State,
        job.ErrorCode, job.CancelRequested, job.CreatedUtc, job.ClaimedUtc, job.EndedUtc
    };
}

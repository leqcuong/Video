using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Processing;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Jobs;

public sealed class ProcessingConflictException(string message) : Exception(message);

public sealed class ProcessingCommands(IDbContextFactory<ThesisDemoDbContext> factory, IJobDispatcher dispatcher, IConfiguration configuration)
{
    public async Task<DurableJob> StartAsync(Guid collectionId, ProcessingRequest request, string? key, Guid? parentId, CancellationToken ct)
    {
        var snapshot = ProcessingValidation.Validate(request,
            configuration["Processing:CaptionModel"] ?? "Salesforce/blip-image-captioning-base",
            configuration["Processing:EmbeddingModel"] ?? "sentence-transformers/all-MiniLM-L6-v2");
        var json = ProcessingValidation.Serialize(snapshot);
        var configHash = ProcessingValidation.Hash(json);
        key = string.IsNullOrWhiteSpace(key) ? Guid.NewGuid().ToString("N") : key.Trim();
        if (key.Length > 128) throw new ArgumentException("Idempotency key is too long.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Collections.AnyAsync(x => x.Id == collectionId, ct)) throw new KeyNotFoundException("Collection not found.");
        var assets = await db.VideoAssets.Where(x => x.CollectionId == collectionId && snapshot.AssetIds.Contains(x.Id)).ToListAsync(ct);
        if (assets.Count != snapshot.AssetIds.Length || assets.Any(x => x.MediaArtifactId is null))
            throw new ArgumentException("All selected assets must have registered media and belong to the collection.");
        var mediaIds = assets.Select(x => x.MediaArtifactId!.Value).ToArray();
        var media = await db.Artifacts.AsNoTracking().Where(x => mediaIds.Contains(x.Id))
            .Select(x => new { x.Id, x.RelativePath }).ToListAsync(ct);
        if (media.Count != assets.Count || media.Any(x => !x.RelativePath.StartsWith("managed/", StringComparison.Ordinal)))
            throw new ArgumentException("Live processing requires managed uploaded media; imported legacy media cannot be processed in place.");
        var inputHash = ProcessingValidation.Hash(configHash + string.Join("|", assets.OrderBy(x => x.Id).Select(x => x.Id.ToString("N") + ":" + x.Sha256)));
        var prior = await db.Jobs.SingleOrDefaultAsync(x => x.CollectionId == collectionId && x.IdempotencyKey == key, ct);
        if (prior is not null)
        {
            if (prior.InputHash != inputHash || prior.RequestJson != json) throw new ProcessingConflictException("Idempotency key already has a different request.");
            return prior;
        }
        if (assets.Any(x => x.State is AssetState.Queued or AssetState.Processing))
            throw new ProcessingConflictException("An asset is already being processed.");
        var run = new ProcessingRun(collectionId, RunMode.Local, json, configHash, inputHash);
        run.TransitionTo(RunState.Queued);
        var job = new DurableJob { CollectionId = collectionId, RunId = run.Id, ParentJobId = parentId,
            IdempotencyKey = key, InputHash = inputHash, RequestJson = json };
        foreach (var asset in assets) asset.SetState(AssetState.Queued);
        db.ProcessingRuns.Add(run);
        db.Jobs.Add(job);
        db.JobEvents.Add(new JobEvent { JobId = job.Id, Kind = "job_queued", DetailJson = ProcessingValidation.Serialize(new { totalAssets = assets.Count }) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var check = await factory.CreateDbContextAsync(ct);
            var raced = await check.Jobs.SingleOrDefaultAsync(x => x.CollectionId == collectionId && x.IdempotencyKey == key, ct);
            if (raced is not null && raced.InputHash == inputHash && raced.RequestJson == json) return raced;
            throw;
        }
        await dispatcher.SignalAsync(job.Id, ct);
        return job;
    }

    public async Task<DurableJob> RetryAsync(Guid jobId, bool failedOnly, bool resume, CancellationToken ct)
    {
        if (resume) throw new ArgumentException("In-place resume is not supported. Retry creates a clean isolated attempt.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var previous = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new KeyNotFoundException("Job not found.");
        if (previous.State is not (JobState.Failed or JobState.Partial or JobState.Canceled or JobState.Interrupted))
            throw new ProcessingConflictException("Only completed unsuccessful jobs can be retried.");
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ProcessingSnapshot>(previous.RequestJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var ids = snapshot.AssetIds;
        if (failedOnly)
        {
            var succeeded = await db.ProcessingAssetAttempts.AsNoTracking()
                .Where(x => x.RunId == previous.RunId && x.State == RunState.Succeeded)
                .Select(x => x.AssetId).ToListAsync(ct);
            ids = ids.Except(succeeded).ToArray();
            if (ids.Length == 0) throw new ProcessingConflictException("No failed or incomplete assets to retry.");
        }
        // A retry uses a fresh isolated run. Resume never trusts files from an interrupted attempt.
        return await StartAsync(previous.CollectionId, new ProcessingRequest(ids, Settings: snapshot.Settings,
            PublishPolicy: snapshot.PublishPolicy), Guid.NewGuid().ToString("N"), previous.Id, ct);
    }

    public async Task<DurableJob> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Job not found.");
        if (job.State == JobState.Canceled) return job;
        if (job.State is not (JobState.Queued or JobState.Running)) throw new ProcessingConflictException("Job is terminal.");
        job.CancelRequested = true;
        job.Version = Guid.NewGuid();
        var run = await db.ProcessingRuns.SingleAsync(x => x.Id == job.RunId, ct);
        if (job.State == JobState.Queued)
        {
            job.State = JobState.Canceled;
            job.EndedUtc = DateTimeOffset.UtcNow;
            run.TransitionTo(RunState.Canceled);
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<ProcessingSnapshot>(job.RequestJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            foreach (var asset in await db.VideoAssets.Where(x => snapshot.AssetIds.Contains(x.Id) && x.State == AssetState.Queued).ToListAsync(ct))
                asset.SetState(AssetState.Failed);
        }
        else if (run.State == RunState.Running) run.TransitionTo(RunState.CancelRequested);
        db.JobEvents.Add(new JobEvent { JobId = id, Kind = "cancel_requested" });
        await db.SaveChangesAsync(ct);
        return job;
    }
}

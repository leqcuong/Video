using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Jobs;

public enum JobState { Queued, Running, Succeeded, Partial, Failed, Interrupted, Canceled }
public sealed class DurableJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "processing";
    public Guid CollectionId { get; set; }
    public Guid RunId { get; set; }
    public Guid? ParentJobId { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string InputHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
    public string ProgressJson { get; set; } = "{}";
    public string? ErrorCode { get; set; }
    public bool CancelRequested { get; set; }
    public JobState State { get; set; } = JobState.Queued;
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClaimedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class JobEvent
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Kind { get; set; } = "";
    public string DetailJson { get; set; } = "{}";
}

public interface IDurableJobRepository
{
    Task EnqueueAsync(DurableJob job, CancellationToken ct);
    Task<IReadOnlyList<Guid>> RecoverAsync(CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListQueuedAsync(CancellationToken ct);
    Task<Guid?> ClaimAsync(Guid id, CancellationToken ct);
    Task CompleteAsync(Guid id, Guid claimToken, bool succeeded, CancellationToken ct);
    Task<bool> IsCancellationRequestedAsync(Guid id, CancellationToken ct);
    Task RecordAsync(Guid id, string kind, string detailJson, CancellationToken ct);
    Task FinishAsync(Guid id, Guid claimToken, JobState state, string? errorCode, CancellationToken ct);
}

public sealed class DurableJobRepository(IDbContextFactory<ThesisDemoDbContext> factory) : IDurableJobRepository
{
    public async Task<IReadOnlyList<Guid>> ListQueuedAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Jobs.Where(x => x.State == JobState.Queued).Select(x => x.Id).ToListAsync(ct);
    }
    public async Task EnqueueAsync(DurableJob job, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> RecoverAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lost = await db.Jobs.Where(x => x.State == JobState.Running).ToListAsync(ct);
        foreach (var job in lost)
        {
            job.State = job.CancelRequested ? JobState.Canceled : JobState.Interrupted;
            job.EndedUtc = DateTimeOffset.UtcNow;
            job.Version = Guid.NewGuid();
            var run = await db.ProcessingRuns.SingleOrDefaultAsync(x => x.Id == job.RunId, ct);
            if (run is not null)
            {
                if (job.CancelRequested && run.State == ThesisDemo.Domain.Processing.RunState.Running)
                    run.TransitionTo(ThesisDemo.Domain.Processing.RunState.CancelRequested);
                if (run.State == ThesisDemo.Domain.Processing.RunState.CancelRequested)
                    run.TransitionTo(ThesisDemo.Domain.Processing.RunState.Canceled);
                else if (run.State is ThesisDemo.Domain.Processing.RunState.Queued or ThesisDemo.Domain.Processing.RunState.Running or ThesisDemo.Domain.Processing.RunState.Validating)
                    run.TransitionTo(job.CancelRequested ? ThesisDemo.Domain.Processing.RunState.Canceled : ThesisDemo.Domain.Processing.RunState.Interrupted);
                var request = System.Text.Json.JsonSerializer.Deserialize<ThesisDemo.Application.Processing.ProcessingSnapshot>(job.RequestJson,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                if (request is not null)
                {
                    foreach (var asset in await db.VideoAssets.Where(x => request.AssetIds.Contains(x.Id) &&
                                 (x.State == ThesisDemo.Domain.Library.AssetState.Queued || x.State == ThesisDemo.Domain.Library.AssetState.Processing)).ToListAsync(ct))
                        asset.SetState(ThesisDemo.Domain.Library.AssetState.Failed);
                }
            }
            db.JobEvents.Add(new JobEvent { JobId = job.Id, Kind = "job_interrupted" });
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { /* another worker recovered first */ }
        return await db.Jobs.Where(x => x.State == JobState.Queued).Select(x => x.Id).ToListAsync(ct);
    }
    public async Task<Guid?> ClaimAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id && x.State == JobState.Queued, ct);
        if (job is null) return null;
        job.State = JobState.Running; job.ClaimToken = Guid.NewGuid(); job.ClaimedUtc = DateTimeOffset.UtcNow; job.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); return job.ClaimToken; } catch (DbUpdateConcurrencyException) { return null; }
    }
    public async Task CompleteAsync(Guid id, Guid claimToken, bool succeeded, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (job.ClaimToken != claimToken) throw new InvalidOperationException("Stale or invalid job claim.");
        if (job.State != JobState.Running) return;
        await FinishAsync(id, claimToken, succeeded ? JobState.Succeeded : JobState.Failed, null, ct);
    }

    public async Task<bool> IsCancellationRequestedAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Jobs.Where(x => x.Id == id).Select(x => x.CancelRequested).SingleAsync(ct);
    }

    public async Task RecordAsync(Guid id, string kind, string detailJson, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.JobEvents.Add(new JobEvent { JobId = id, Kind = kind, DetailJson = detailJson });
        await db.SaveChangesAsync(ct);
    }

    public async Task FinishAsync(Guid id, Guid claimToken, JobState state, string? errorCode, CancellationToken ct)
    {
        if (state is not (JobState.Succeeded or JobState.Partial or JobState.Failed or JobState.Canceled))
            throw new ArgumentOutOfRangeException(nameof(state));
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id && x.State == JobState.Running && x.ClaimToken == claimToken, ct)
            ?? throw new InvalidOperationException("Stale or invalid job claim.");
        job.State = job.CancelRequested ? JobState.Canceled : state;
        job.ErrorCode = errorCode; job.EndedUtc = DateTimeOffset.UtcNow; job.Version = Guid.NewGuid();
        db.JobEvents.Add(new JobEvent { JobId = id, Kind = "job_finished", DetailJson = System.Text.Json.JsonSerializer.Serialize(new { state = job.State.ToString(), errorCode }) });
        await db.SaveChangesAsync(ct);
    }
}

public interface IJobDispatcher { ValueTask SignalAsync(Guid id, CancellationToken ct = default); }
public sealed class JobDispatcher : IJobDispatcher
{
    internal Channel<Guid> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<Guid>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropWrite });
    public ValueTask SignalAsync(Guid id, CancellationToken ct = default) => Channel.Writer.WriteAsync(id, ct);
}

public sealed class DurableJobWorker(IServiceScopeFactory scopes, JobDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
            foreach (var id in await scope.ServiceProvider.GetRequiredService<IDurableJobRepository>().RecoverAsync(stoppingToken))
                await dispatcher.SignalAsync(id, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            while (dispatcher.Channel.Reader.TryRead(out var id)) await RunAsync(id, stoppingToken);
            using var pollTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            pollTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await dispatcher.Channel.Reader.WaitToReadAsync(pollTimeout.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { await SweepAsync(stoppingToken); }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        foreach (var id in await scope.ServiceProvider.GetRequiredService<IDurableJobRepository>().ListQueuedAsync(ct))
            await dispatcher.SignalAsync(id, ct);
    }

    private async Task RunAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
        var token = await repository.ClaimAsync(id, ct);
        if (token is null) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<IJobHandler>().ExecuteAsync(id, token.Value, ct);
            await repository.CompleteAsync(id, token.Value, true, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A host stop is recoverable. The next startup marks the claimed job
            // and its in-flight assets interrupted, rather than claiming user cancellation.
            throw;
        }
        catch (OperationCanceledException) { await repository.FinishAsync(id, token.Value, JobState.Canceled, "CANCELED", CancellationToken.None); }
        catch (Exception ex)
        {
            await repository.RecordAsync(id, "job_error", System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }), CancellationToken.None);
            await repository.FinishAsync(id, token.Value, JobState.Failed, "PROCESSING_FAILED", CancellationToken.None);
        }
    }
}

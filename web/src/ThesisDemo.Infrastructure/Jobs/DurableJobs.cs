using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Jobs;

public enum JobState { Queued, Running, Succeeded, Failed, Interrupted, Canceled }
public sealed class DurableJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "processing";
    public JobState State { get; set; } = JobState.Queued;
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClaimedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public interface IDurableJobRepository
{
    Task EnqueueAsync(DurableJob job, CancellationToken ct);
    Task<IReadOnlyList<Guid>> RecoverAsync(CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListQueuedAsync(CancellationToken ct);
    Task<Guid?> ClaimAsync(Guid id, CancellationToken ct);
    Task CompleteAsync(Guid id, Guid claimToken, bool succeeded, CancellationToken ct);
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
        foreach (var job in lost) { job.State = JobState.Interrupted; job.EndedUtc = DateTimeOffset.UtcNow; job.Version = Guid.NewGuid(); }
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
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id && x.State == JobState.Running && x.ClaimToken == claimToken, ct)
            ?? throw new InvalidOperationException("Stale or invalid job claim.");
        job.State = succeeded ? JobState.Succeeded : JobState.Failed; job.EndedUtc = DateTimeOffset.UtcNow; job.Version = Guid.NewGuid();
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
            await repository.CompleteAsync(id, token.Value, true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { await repository.CompleteAsync(id, token.Value, false, CancellationToken.None); }
    }
}

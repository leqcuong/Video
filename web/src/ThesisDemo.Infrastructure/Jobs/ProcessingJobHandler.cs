using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Application.Processing;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Infrastructure.Legacy;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Infrastructure.Jobs;

public sealed class ProcessingJobHandler(
    IDbContextFactory<ThesisDemoDbContext> factory, ManagedStorage storage,
    PythonPipelineBridge bridge, SnapshotPublisher publisher,
    IDurableJobRepository jobs) : IJobHandler
{
    public async Task ExecuteAsync(Guid jobId, Guid claimToken, CancellationToken ct)
    {
        Guid runId;
        ProcessingSnapshot snapshot;
        List<PipelineAsset> inputs;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var job = await db.Jobs.SingleAsync(x => x.Id == jobId && x.ClaimToken == claimToken, ct);
            runId = job.RunId;
            snapshot = JsonSerializer.Deserialize<ProcessingSnapshot>(job.RequestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var run = await db.ProcessingRuns.SingleAsync(x => x.Id == runId, ct);
            run.TransitionTo(RunState.Running);
            var assets = await db.VideoAssets.Where(x => snapshot.AssetIds.Contains(x.Id)).ToListAsync(ct);
            inputs = [];
            foreach (var asset in assets)
            {
                if (asset.State != AssetState.Queued || asset.MediaArtifactId is null)
                    throw new InvalidOperationException("Asset state or media changed after queueing.");
                var artifact = await db.Artifacts.SingleAsync(x => x.Id == asset.MediaArtifactId, ct);
                inputs.Add(new PipelineAsset(asset.Id, asset.LegacyAssetId,
                    artifact.RelativePath.StartsWith("managed/", StringComparison.Ordinal)
                        ? storage.Resolve(artifact.RelativePath)
                        : throw new InvalidOperationException("Processing requires managed uploaded media.")));
                asset.SetState(AssetState.Processing);
            }
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var outcome = await bridge.ExecuteAsync(jobId, runId, snapshot, inputs, storage.Root, ct);
            if (await jobs.IsCancellationRequestedAsync(jobId, ct)) throw new OperationCanceledException("Canceled by user.");
            await using var db = await factory.CreateDbContextAsync(ct);
            var run = await db.ProcessingRuns.SingleAsync(x => x.Id == runId, ct);
            run.TransitionTo(RunState.Validating);
            var assets = await db.VideoAssets.Where(x => snapshot.AssetIds.Contains(x.Id)).ToListAsync(ct);
            var successes = new List<VideoAsset>();
            foreach (var asset in assets)
            {
                var ok = outcome.Outcomes.TryGetValue(asset.Id, out var passed) && passed;
                var attempt = new ProcessingAssetAttempt(runId, asset.Id, claimToken, outcome.OutputRoot);
                attempt.Complete(ok ? RunState.Succeeded : RunState.Failed, ok ? null : "PIPELINE_ASSET_FAILED");
                db.ProcessingAssetAttempts.Add(attempt);
                asset.SetState(ok ? AssetState.Ready : AssetState.Failed);
                if (ok) successes.Add(asset);
            }
            var allSucceeded = successes.Count == assets.Count && outcome.ExitCode == 0;
            if (successes.Count > 0)
                await publisher.ComposeAsync(db, runId, snapshot, outcome, successes, allSucceeded, ct);
            run.TransitionTo(allSucceeded ? RunState.Succeeded : successes.Count > 0 ? RunState.Partial : RunState.Failed);
            await db.SaveChangesAsync(ct);
            await jobs.FinishAsync(jobId, claimToken,
                allSucceeded ? JobState.Succeeded : successes.Count > 0 ? JobState.Partial : JobState.Failed,
                allSucceeded ? null : "PARTIAL_OR_FAILED_ASSETS", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Preserve the claimed state so RecoverAsync can reconcile an interrupted host.
            throw;
        }
        catch
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            var run = await db.ProcessingRuns.SingleAsync(x => x.Id == runId);
            var canceled = await db.Jobs.AnyAsync(x => x.Id == jobId && x.CancelRequested);
            if (run.State == RunState.Running)
            {
                if (canceled) run.TransitionTo(RunState.CancelRequested);
                run.TransitionTo(canceled ? RunState.Canceled : RunState.Failed);
            }
            else if (run.State == RunState.CancelRequested) run.TransitionTo(RunState.Canceled);
            else if (run.State == RunState.Validating) run.TransitionTo(canceled ? RunState.Canceled : RunState.Failed);
            foreach (var asset in await db.VideoAssets.Where(x => snapshot.AssetIds.Contains(x.Id) &&
                         (x.State == AssetState.Processing || x.State == AssetState.Queued)).ToListAsync())
                asset.SetState(AssetState.Failed);
            await db.SaveChangesAsync();
            throw;
        }
    }
}

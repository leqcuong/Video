using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Processing;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Infrastructure.Jobs;
using ThesisDemo.Infrastructure.Legacy;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.IntegrationTests.Jobs;

public sealed class ProcessingLifecycleTests
{
    private static ProviderDbContextFactory CreateFactory() => new(DatabaseProvider.Sqlite,
        $"Data Source={Path.Combine(Path.GetTempPath(), $"thesis-jobs-{Guid.NewGuid():N}.db")}");

    [Fact]
    public async Task Upgrade_preserves_multiple_legacy_jobs_with_distinct_keys()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync("20261011111358_AddRecordedResearch");
        for (var i = 0; i < 2; i++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Jobs (Id, Kind, State, CreatedUtc, Version) VALUES ({Guid.NewGuid()}, {"processing"}, {0}, {DateTimeOffset.UtcNow}, {Guid.NewGuid()})");
        }
        await db.Database.MigrateAsync();
        var jobs = await db.Jobs.AsNoTracking().ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(2, jobs.Select(x => x.IdempotencyKey).Distinct().Count());
        Assert.All(jobs, x => Assert.StartsWith("legacy-", x.IdempotencyKey));
    }

    [Theory]
    [InlineData(false, JobState.Interrupted, RunState.Interrupted)]
    [InlineData(true, JobState.Canceled, RunState.Canceled)]
    public async Task Recovery_reconciles_running_job_run_and_asset(bool canceled, JobState jobState, RunState runState)
    {
        var factory = CreateFactory();
        Guid jobId, assetId, runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            var collection = new Collection("recovery");
            var asset = NewAsset(collection.Id);
            asset.SetState(AssetState.Queued);
            asset.SetState(AssetState.Processing);
            var snapshot = ProcessingValidation.Validate(new ProcessingRequest([asset.Id]), "caption", "embedding");
            var run = new ProcessingRun(collection.Id, RunMode.Local, ProcessingValidation.Serialize(snapshot), "config", "input");
            run.TransitionTo(RunState.Queued);
            run.TransitionTo(RunState.Running);
            if (canceled) run.TransitionTo(RunState.CancelRequested);
            var job = new DurableJob
            {
                CollectionId = collection.Id, RunId = run.Id, RequestJson = ProcessingValidation.Serialize(snapshot),
                State = JobState.Running, ClaimToken = Guid.NewGuid(), CancelRequested = canceled
            };
            db.Collections.Add(collection);
            db.VideoAssets.Add(asset);
            db.ProcessingRuns.Add(run);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id; assetId = asset.Id; runId = run.Id;
        }
        var repo = new DurableJobRepository(factory);
        await repo.RecoverAsync(default);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(jobState, (await check.Jobs.SingleAsync(x => x.Id == jobId)).State);
        Assert.Equal(runState, (await check.ProcessingRuns.SingleAsync(x => x.Id == runId)).State);
        Assert.Equal(AssetState.Failed, (await check.VideoAssets.SingleAsync(x => x.Id == assetId)).State);
        Assert.Equal(1, await check.JobEvents.CountAsync(x => x.JobId == jobId && x.Kind == "job_interrupted"));
    }

    [Fact]
    public async Task Start_is_idempotent_cancel_is_terminal_and_retry_is_isolated()
    {
        var factory = CreateFactory();
        Guid collectionId, assetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            var collection = new Collection("queue");
            var asset = NewAsset(collection.Id);
            var artifact = new ArtifactRecord(Guid.NewGuid(), "media", $"managed/media/{asset.Id:N}.webm",
                new string('a', 64), 10, "video/webm", "{}");
            asset.AttachMedia(artifact.Id);
            db.Collections.Add(collection); db.VideoAssets.Add(asset); db.Artifacts.Add(artifact);
            await db.SaveChangesAsync();
            collectionId = collection.Id; assetId = asset.Id;
        }
        var commands = new ProcessingCommands(factory, new JobDispatcher(), new ConfigurationBuilder().Build());
        var request = new ProcessingRequest([assetId]);
        var original = await commands.StartAsync(collectionId, request, "test-key", null, default);
        var repeat = await commands.StartAsync(collectionId, request, "test-key", null, default);
        Assert.Equal(original.Id, repeat.Id);
        await Assert.ThrowsAsync<ProcessingConflictException>(() => commands.StartAsync(collectionId,
            new ProcessingRequest([assetId], PublishPolicy: "onSuccess"), "test-key", null, default));
        await commands.CancelAsync(original.Id, default);
        await using (var check = await factory.CreateDbContextAsync())
        {
            Assert.Equal(JobState.Canceled, (await check.Jobs.SingleAsync(x => x.Id == original.Id)).State);
            Assert.Equal(AssetState.Failed, (await check.VideoAssets.SingleAsync(x => x.Id == assetId)).State);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => commands.RetryAsync(original.Id, true, true, default));
        var retry = await commands.RetryAsync(original.Id, true, false, default);
        Assert.NotEqual(original.RunId, retry.RunId);
        Assert.Equal(original.Id, retry.ParentJobId);
        Assert.Equal(JobState.Queued, retry.State);
    }

    [Fact]
    public async Task Partial_publication_stays_inactive_and_hash_fallback_is_rejected()
    {
        var factory = CreateFactory();
        var root = Path.Combine(Path.GetTempPath(), $"thesis-publish-{Guid.NewGuid():N}");
        var storage = new ManagedStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ManagedRoot"] = root
        }).Build());
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            var collection = new Collection("publication");
            var asset = NewAsset(collection.Id);
            asset.SetState(AssetState.Queued); asset.SetState(AssetState.Processing); asset.SetState(AssetState.Ready);
            var config = ProcessingValidation.Validate(new ProcessingRequest([asset.Id]), "caption", "embedding");
            var run = new ProcessingRun(collection.Id, RunMode.Local, ProcessingValidation.Serialize(config), "config", "input");
            var attempt = new ProcessingAssetAttempt(run.Id, asset.Id, Guid.NewGuid(), root);
            attempt.Complete(RunState.Succeeded);
            db.Collections.Add(collection); db.VideoAssets.Add(asset); db.ProcessingRuns.Add(run);
            db.ProcessingAssetAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var output = Path.Combine(root, "stage");
            Directory.CreateDirectory(Path.Combine(output, asset.Id.ToString("N")));
            Directory.CreateDirectory(Path.Combine(output, "index"));
            var row = new { asset_id = asset.Id.ToString("N"), segment_id = "1", start_sec = 0.0,
                end_sec = 2.0, caption = "sample caption", embedding = new[] { 0.1, 0.2 } };
            await File.WriteAllTextAsync(Path.Combine(output, asset.Id.ToString("N"), "segments.json"),
                JsonSerializer.Serialize(new[] { row }));
            await File.WriteAllTextAsync(Path.Combine(output, "index", "combined_index.json"),
                JsonSerializer.Serialize(new[] { row }));
            var publisher = new SnapshotPublisher(storage);
            await Assert.ThrowsAsync<InvalidDataException>(() => publisher.ComposeAsync(db, run.Id, config,
                new PipelineOutcome(0, new Dictionary<Guid, bool> { [asset.Id] = true }, output,
                    "hash-fallback", "embedding"), [asset], true, default));
            Assert.Null(collection.ActiveIndexId);

            await File.WriteAllTextAsync(Path.Combine(output, "index", "combined_index.json"),
                JsonSerializer.Serialize(new[] { new { row.asset_id, row.segment_id, start_sec = 0.5, row.end_sec,
                    row.caption, row.embedding } }));
            await Assert.ThrowsAsync<InvalidDataException>(() => publisher.ComposeAsync(db, run.Id, config,
                new PipelineOutcome(0, new Dictionary<Guid, bool> { [asset.Id] = true }, output,
                    "sentence-transformers", "embedding"), [asset], true, default));
            Assert.Null(collection.ActiveIndexId);
            db.ChangeTracker.Clear();
            await File.WriteAllTextAsync(Path.Combine(output, "index", "combined_index.json"),
                JsonSerializer.Serialize(new[] { row }));

            var snapshot = await publisher.ComposeAsync(db, run.Id, config,
                new PipelineOutcome(1, new Dictionary<Guid, bool> { [asset.Id] = true }, output,
                    "sentence-transformers", "embedding"), [asset], false, default);
            await db.SaveChangesAsync();
            Assert.Null(collection.ActiveIndexId);
            Assert.Equal(1, snapshot.RowCount);
            Assert.Equal(1, await db.IndexMembers.CountAsync(x => x.IndexId == snapshot.Id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static VideoAsset NewAsset(Guid collectionId) => new(collectionId, Guid.NewGuid().ToString("N"),
        "clip.webm", new string('a', 64), 20, 25, 320, 240);
}

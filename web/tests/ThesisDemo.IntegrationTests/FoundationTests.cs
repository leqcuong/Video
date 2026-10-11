using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThesisDemo.Domain.Library;
using ThesisDemo.Infrastructure.Jobs;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.IntegrationTests;

public sealed class FoundationTests
{
    [Fact]
    public async Task SqlServer_rowversion_concurrency_works_when_localdb_available()
    {
        if (Environment.GetEnvironmentVariable("THESIS_TEST_LOCALDB") != "1") return;
        var name = $"ThesisDemoTest{Guid.NewGuid():N}";
        var factory = new ProviderDbContextFactory(DatabaseProvider.SqlServer,
            $"Server=(localdb)\\MSSQLLocalDB;Database={name};Trusted_Connection=True;TrustServerCertificate=True");
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            db.Collections.Add(new Collection("original")); await db.SaveChangesAsync();
        }
        await using var one = await factory.CreateDbContextAsync();
        await using var two = await factory.CreateDbContextAsync();
        var a = await one.Collections.SingleAsync();
        var b = await two.Collections.SingleAsync();
        a.Rename("updated"); await one.SaveChangesAsync();
        b.Rename("stale");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_migration_and_guid_concurrency_work()
    {
        var path = Path.Combine(Path.GetTempPath(), $"thesis-{Guid.NewGuid():N}.db");
        var factory = new ProviderDbContextFactory(DatabaseProvider.Sqlite, $"Data Source={path}");
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            db.Collections.Add(new Collection("first"));
            await db.SaveChangesAsync();
        }
        await using var one = await factory.CreateDbContextAsync();
        await using var two = await factory.CreateDbContextAsync();
        var a = await one.Collections.SingleAsync();
        var b = await two.Collections.SingleAsync();
        a.Rename("second"); await one.SaveChangesAsync();
        b.Rename("stale");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());
    }

    [Fact]
    public async Task Job_claim_recovery_and_stale_completion_work()
    {
        var path = Path.Combine(Path.GetTempPath(), $"thesis-jobs-{Guid.NewGuid():N}.db");
        var factory = new ProviderDbContextFactory(DatabaseProvider.Sqlite, $"Data Source={path}");
        await using (var db = await factory.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var repo = new DurableJobRepository(factory);
        var job = new DurableJob(); await repo.EnqueueAsync(job, default);
        Assert.Contains(job.Id, await repo.ListQueuedAsync(default));
        var token = await repo.ClaimAsync(job.Id, default);
        Assert.NotNull(token);
        Assert.Null(await repo.ClaimAsync(job.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CompleteAsync(job.Id, Guid.NewGuid(), true, default));
        await repo.RecoverAsync(default);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(JobState.Interrupted, (await check.Jobs.SingleAsync()).State);
        Assert.Empty(await repo.ListQueuedAsync(default));
    }

    [Fact]
    public async Task Worker_polls_queued_rows_even_without_channel_signal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"thesis-worker-{Guid.NewGuid():N}.db");
        var factory = new ProviderDbContextFactory(DatabaseProvider.Sqlite, $"Data Source={path}");
        await using (var db = await factory.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<ThesisDemoDbContext>>(factory);
        services.AddScoped<IDurableJobRepository, DurableJobRepository>();
        services.AddSingleton<JobDispatcher>();
        services.AddScoped<ThesisDemo.Application.Artifacts.IJobHandler, SucceedingJobHandler>();
        using var provider = services.BuildServiceProvider();
        using var worker = new DurableJobWorker(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<JobDispatcher>());
        await worker.StartAsync(default);
        await Task.Delay(750);
        var job = new DurableJob();
        await new DurableJobRepository(factory).EnqueueAsync(job, default);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(9);
        JobState state;
        do
        {
            await Task.Delay(200);
            await using var db = await factory.CreateDbContextAsync();
            state = (await db.Jobs.SingleAsync(x => x.Id == job.Id)).State;
        } while (state != JobState.Succeeded && DateTimeOffset.UtcNow < deadline);
        await worker.StopAsync(default);
        Assert.Equal(JobState.Succeeded, state);
    }

    [Fact]
    public async Task Api_uses_antiforgery_and_rejects_foreign_origin()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var health = await client.GetAsync("/api/v1/system/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var noToken = await client.PostAsJsonAsync("/api/v1/system/validate", new { name = "ok" });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal("HTTP_400", (await noToken.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        var bootstrap = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/security/antiforgery");
        var token = bootstrap.GetProperty("token").GetString();
        using var valid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/system/validate");
        valid.Headers.Add("Origin", "http://127.0.0.1:5074");
        valid.Headers.Add("X-CSRF-TOKEN", token);
        valid.Content = JsonContent.Create(new { name = "ok" });
        var validResponse = await client.SendAsync(valid);
        Assert.True(validResponse.StatusCode == HttpStatusCode.OK, await validResponse.Content.ReadAsStringAsync());
        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/system/validate");
        invalid.Headers.Add("X-CSRF-TOKEN", token);
        invalid.Content = JsonContent.Create(new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/v1/system/validate");
        foreign.Headers.Add("Origin", "http://127.0.0.1:9999");
        foreign.Content = JsonContent.Create(new { name = "ok" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
    }
}

public sealed class SucceedingJobHandler : ThesisDemo.Application.Artifacts.IJobHandler
{
    public Task ExecuteAsync(Guid jobId, Guid claimToken, CancellationToken cancellationToken) => Task.CompletedTask;
}

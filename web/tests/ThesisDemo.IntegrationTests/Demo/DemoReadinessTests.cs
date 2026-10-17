using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Domain.Library;
using ThesisDemo.Infrastructure.Demo;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.IntegrationTests.Demo;

public sealed class DemoReadinessTests
{
    [Fact]
    public async Task Unattached_media_and_absent_active_index_are_not_ready()
    {
        var root = Path.Combine(Path.GetTempPath(), $"demo-preflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var factory = new ProviderDbContextFactory(DatabaseProvider.Sqlite,
                $"Data Source={Path.Combine(root, "demo.db")}");
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
                var collection = new Collection("offline");
                db.Collections.Add(collection);
                db.VideoAssets.Add(new VideoAsset(collection.Id, "clip", "clip.mp4", new string('a', 64), 20, 25, 320, 240));
                await db.SaveChangesAsync();
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ManagedRoot"] = root,
                ["Legacy:RegisteredSources:0:Id"] = "fixture",
                ["Legacy:RegisteredSources:0:MediaRoot"] = root,
                ["Legacy:RegisteredSources:0:ArtifactRoot"] = root
            }).Build();
            var result = await new DemoReadiness(factory, new ManagedStorage(configuration),
                new LocalMediaStore(configuration)).CheckAsync(default);
            Assert.False(result.ReadyForOfflineSearch);
            Assert.Contains(result.Checks, x => x.Name == "Media files" && x.Status == "blocked");
            Assert.Contains(result.Checks, x => x.Name == "Active index" && x.Status == "blocked");
            Assert.Contains(result.Checks, x => x.Name == "Colab handoff" && x.Status == "unverified");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}

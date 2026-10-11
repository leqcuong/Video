using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Collections;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.IntegrationTests.Imports;

public sealed class ImportApiTests
{
    [Fact]
    public async Task V01_import_preview_commit_and_repeat_are_idempotent()
    {
        using var app = CreateFactory("V01", "v01");
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token, "Fixture V01");

        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new
        {
            registeredSourceId = "v01",
            action = "preview"
        }, HttpStatusCode.Accepted);
        Assert.Equal(2, preview.AssetsValid);
        Assert.Equal(3, preview.SegmentsDiscovered);
        Assert.Empty(preview.Diagnostics);

        var commit = await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new
        {
            registeredSourceId = "v01",
            action = "commit",
            previewId = preview.PreviewId
        }, HttpStatusCode.OK);
        Assert.Equal(2, commit.AssetsImported);
        Assert.Equal(3, commit.SegmentsImported);

        var repeat = await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new
        {
            registeredSourceId = "v01",
            action = "commit",
            previewId = preview.PreviewId
        }, HttpStatusCode.OK);
        Assert.Equal(0, repeat.AssetsImported);
        Assert.Equal(2, repeat.AssetsSkipped);

        var assets = await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{collection.Id}/assets");
        Assert.NotNull(assets);
        Assert.Equal(2, assets!.Total);
        Assert.All(assets.Items, x => Assert.Equal("available", x.MediaAvailability));
    }

    [Fact]
    public async Task V02_preview_reports_missing_media_and_commit_skips_invalid_rows()
    {
        using var app = CreateFactory("V02", "v02");
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token, "Fixture V02");

        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new
        {
            registeredSourceId = "v02",
            action = "preview"
        }, HttpStatusCode.Accepted);
        Assert.Equal(1, preview.AssetsInvalid);
        Assert.Contains(preview.Diagnostics, x => x.Contains("media file is missing", StringComparison.OrdinalIgnoreCase));

        var commit = await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new
        {
            registeredSourceId = "v02",
            action = "commit",
            previewId = preview.PreviewId
        }, HttpStatusCode.OK);
        Assert.Equal(0, commit.AssetsImported);
        Assert.Equal(0, commit.SegmentsImported);
    }

    [Fact]
    public async Task V01_restart_preserves_asset_ids_and_source_hashes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"thesis-restart-{Guid.NewGuid():N}.db");
        Guid collectionId;
        Dictionary<string, Guid> before;
        using (var app = CreateFactory("V01", "v01", dbPath))
        using (var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") }))
        {
            var token = await GetTokenAsync(client);
            collectionId = (await CreateCollectionAsync(client, token, "Persistent collection")).Id;
            var preview = await PostImportAsync<ImportPreviewResult>(client, token, collectionId, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
            Assert.All(preview.Assets, row => Assert.Equal(64, row.Sha256?.Length));
            await PostImportAsync<ImportCommitResult>(client, token, collectionId, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);
            before = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{collectionId}/assets"))!.Items.ToDictionary(x => x.LegacyAssetId, x => x.Id);
            await using var db = await app.Services.GetRequiredService<IDbContextFactory<ThesisDemoDbContext>>().CreateDbContextAsync();
            var registeredMedia = await db.Artifacts.Where(x => x.Kind == "raw-media").ToListAsync();
            Assert.Equal(2, registeredMedia.Count);
            foreach (var artifact in registeredMedia)
            {
                Assert.Equal(64, artifact.Sha256.Length);
                Assert.Contains("sourceHashes", artifact.ProvenanceJson);
                Assert.Contains("v01", artifact.ProvenanceJson);
            }
        }

        using (var restarted = CreateFactory("V01", "v01", dbPath))
        using (var client = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") }))
        {
            var token = await GetTokenAsync(client);
            var assets = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{collectionId}/assets"))!;
            Assert.Equal(2, assets.Total);
            Assert.All(assets.Items, row => Assert.Equal(before[row.LegacyAssetId], row.Id));
            var preview = await PostImportAsync<ImportPreviewResult>(client, token, collectionId, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
            var repeat = await PostImportAsync<ImportCommitResult>(client, token, collectionId, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);
            Assert.Equal(0, repeat.AssetsImported);
            Assert.Equal(2, repeat.AssetsSkipped);
            Assert.Equal(0, repeat.AssetsConflicted);
        }
    }

    [Fact]
    public async Task V01_filename_collision_is_reported_without_modifying_source()
    {
        var source = Path.Combine(FindRepositoryRoot(), "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", "V01");
        var fixture = Path.Combine(Path.GetTempPath(), $"thesis-collision-{Guid.NewGuid():N}");
        CopyDirectory(source, fixture);
        try
        {
            var metadataPath = Path.Combine(fixture, "artifacts", "clip_b", "asset_metadata.json");
            var metadata = await File.ReadAllTextAsync(metadataPath);
            await File.WriteAllTextAsync(metadataPath, metadata.Replace("clip_b.mp4", "clip_a.mp4", StringComparison.Ordinal));
            using var app = CreateFactory("V01", "v01", fixtureRoot: fixture);
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
            var token = await GetTokenAsync(client);
            var id = (await CreateCollectionAsync(client, token, "Collision fixture")).Id;
            var preview = await PostImportAsync<ImportPreviewResult>(client, token, id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
            Assert.Contains(preview.Diagnostics, x => x.Contains("filename collision", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, preview.AssetsValid);
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task V02_changed_preview_id_is_rejected_and_pagination_is_stable()
    {
        using var app = CreateFactory("V01", "v01");
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var id = (await CreateCollectionAsync(client, token, "Paged assets")).Id;
        var preview = await PostImportAsync<ImportPreviewResult>(client, token, id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
        using var stale = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{id}/imports");
        stale.Headers.Add("Origin", "http://127.0.0.1:5074");
        stale.Headers.Add("X-CSRF-TOKEN", token);
        stale.Content = JsonContent.Create(new { registeredSourceId = "v01", action = "commit", previewId = "stale-hash" });
        var staleResponse = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("IMPORT_CONFLICT", (await staleResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        await PostImportAsync<ImportCommitResult>(client, token, id, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);
        var one = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{id}/assets?page=1&pageSize=1"))!;
        var two = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{id}/assets?page=2&pageSize=1"))!;
        Assert.Equal(2, one.Total);
        Assert.Equal(2, two.Total);
        Assert.NotEqual(Assert.Single(one.Items).Id, Assert.Single(two.Items).Id);
        var named = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{id}/assets?name=clip_a"))!;
        Assert.Equal("clip_a", Assert.Single(named.Items).LegacyAssetId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/collections/{id}/assets?state=not-a-state")).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string fixtureName, string sourceId, string? dbPath = null, string? fixtureRoot = null)
    {
        var repo = FindRepositoryRoot();
        var fixture = fixtureRoot ?? Path.Combine(repo, "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", fixtureName);
        dbPath ??= Path.Combine(Path.GetTempPath(), $"thesis-import-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                    ["Legacy:RegisteredSources:0:Id"] = sourceId,
                    ["Legacy:RegisteredSources:0:MediaRoot"] = Path.Combine(fixture, "media"),
                    ["Legacy:RegisteredSources:0:ArtifactRoot"] = Path.Combine(fixture, "artifacts")
                });
            });
        });
    }


    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var bootstrap = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/security/antiforgery");
        return bootstrap.GetProperty("token").GetString()!;
    }

    private static async Task<CollectionDto> CreateCollectionAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/collections");
        request.Headers.Add("Origin", "http://127.0.0.1:5074");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(new { name });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CollectionDto>())!;
    }

    private static async Task<T> PostImportAsync<T>(HttpClient client, string token, Guid collectionId, object body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{collectionId}/imports");
        request.Headers.Add("Origin", "http://127.0.0.1:5074");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "web")) && Directory.Exists(Path.Combine(current.FullName, "specs")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

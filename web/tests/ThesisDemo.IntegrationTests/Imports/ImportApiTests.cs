using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Collections;

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

    private static WebApplicationFactory<Program> CreateFactory(string fixtureName, string sourceId)
    {
        var repo = FindRepositoryRoot();
        var fixture = Path.Combine(repo, "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", fixtureName);
        var dbPath = Path.Combine(Path.GetTempPath(), $"thesis-import-{Guid.NewGuid():N}.db");
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

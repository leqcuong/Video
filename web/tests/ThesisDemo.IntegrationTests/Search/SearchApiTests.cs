using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Collections;
using ThesisDemo.Application.Search;

namespace ThesisDemo.IntegrationTests.Search;

public sealed class SearchApiTests
{
    [Fact]
    public async Task Search_validates_request_and_maps_legacy_results_to_web_urls()
    {
        using var app = CreateFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token);
        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
        await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);

        var response = await PostSearchAsync(client, token, collection.Id, new
        {
            query = "rocket water",
            topK = 2,
            candidateK = 3,
            hybridAlpha = 0.6,
            mode = "baseline",
            assetIds = Array.Empty<Guid>()
        }, HttpStatusCode.OK);

        var first = Assert.Single(response.BaselineResults!.Take(1));
        Assert.Equal("clip_a", first.LegacyAssetId);
        Assert.Equal("seg_0002", first.LegacySegmentId);
        Assert.StartsWith("/api/v1/assets/", first.MediaUrl, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, first.AssetId);
        Assert.NotEqual(Guid.Empty, first.SegmentId);

        var compare = await PostSearchAsync(client, token, collection.Id, new
        {
            query = "rocket water",
            topK = 2,
            candidateK = 3,
            hybridAlpha = 0.6,
            mode = "compare",
            modelId = "lexical-fallback",
            assetIds = Array.Empty<Guid>()
        }, HttpStatusCode.OK);
        Assert.NotNull(compare.BaselineResults);
        Assert.NotNull(compare.RerankedResults);
        Assert.Equal(compare.BaselineResults![0].StartSec, compare.RerankedResults![0].StartSec);
        Assert.Equal("reranker", compare.RerankedResults![0].ScoreKind);
        Assert.NotNull(compare.RerankedResults![0].BaselineRank);

        using var bad = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{collection.Id}/search");
        bad.Headers.Add("Origin", "http://127.0.0.1:5074");
        bad.Headers.Add("X-CSRF-TOKEN", token);
        bad.Content = JsonContent.Create(new { query = "", topK = 0, candidateK = 0, hybridAlpha = 2.0 });
        var badResponse = await client.SendAsync(bad);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
    }

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.6, 2)]
    [InlineData(1.0, 10)]
    public async Task V03_hybrid_modes_return_repeatable_ranked_segments(double alpha, int topK)
    {
        using var app = CreateFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token);
        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
        await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);
        var body = new { query = "rocket water", topK, candidateK = Math.Max(10, topK), hybridAlpha = alpha, mode = "baseline" };
        var first = await PostSearchAsync(client, token, collection.Id, body, HttpStatusCode.OK);
        var second = await PostSearchAsync(client, token, collection.Id, body, HttpStatusCode.OK);
        Assert.Equal(alpha, first.EffectiveConfig.HybridAlpha, 6);
        Assert.Equal(topK, first.EffectiveConfig.TopK);
        Assert.NotEmpty(first.BaselineResults!);
        Assert.True(first.BaselineResults!.Count <= topK);
        Assert.Equal(first.BaselineResults.Select(x => (x.LegacyAssetId, x.LegacySegmentId)),
            second.BaselineResults!.Select(x => (x.LegacyAssetId, x.LegacySegmentId)));
        Assert.Equal(first.BaselineResults.Select(x => x.Score), second.BaselineResults!.Select(x => x.Score));
        Assert.All(first.BaselineResults, x =>
        {
            Assert.NotEqual(Guid.Empty, x.AssetId);
            Assert.True(x.EndSec > x.StartSec);
            Assert.True(double.IsFinite(x.Score));
            Assert.StartsWith("/api/v1/assets/", x.MediaUrl);
        });
    }

    [Fact]
    public async Task V03_asset_filter_and_parameter_errors_are_enforced()
    {
        using var app = CreateFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token);
        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
        await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);
        var assets = (await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{collection.Id}/assets"))!;
        var clipB = Assert.Single(assets.Items, x => x.LegacyAssetId == "clip_b");
        var scoped = await PostSearchAsync(client, token, collection.Id, new
        {
            query = "rocket water", topK = 10, candidateK = 10, hybridAlpha = 0.6,
            mode = "baseline", assetIds = new[] { clipB.Id }
        }, HttpStatusCode.OK);
        Assert.NotEmpty(scoped.BaselineResults!);
        Assert.All(scoped.BaselineResults!, x => Assert.Equal(clipB.Id, x.AssetId));

        foreach (var body in new object[]
        {
            new { query = "rocket", topK = 2, candidateK = 1, hybridAlpha = 0.6 },
            new { query = "rocket", topK = 1, candidateK = 1, hybridAlpha = -0.1 },
            new { query = "rocket", topK = 1, candidateK = 1, hybridAlpha = 1.1 },
            new { query = "rocket", topK = 1, candidateK = 1, hybridAlpha = 0.6, mode = "compare" }
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{collection.Id}/search");
            request.Headers.Add("Origin", "http://127.0.0.1:5074");
            request.Headers.Add("X-CSRF-TOKEN", token);
            request.Content = JsonContent.Create(body);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var foreign = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{collection.Id}/search");
        foreign.Headers.Add("Origin", "http://127.0.0.1:5074");
        foreign.Headers.Add("X-CSRF-TOKEN", token);
        foreign.Content = JsonContent.Create(new { query = "rocket", topK = 1, candidateK = 1, hybridAlpha = 0.6, assetIds = new[] { Guid.NewGuid() } });
        var foreignResponse = await client.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Conflict, foreignResponse.StatusCode);
        Assert.Equal("SEARCH_CONFLICT", (await foreignResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var repo = FindRepositoryRoot();
        var fixture = Path.Combine(repo, "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", "V01");
        var dbPath = Path.Combine(Path.GetTempPath(), $"thesis-search-api-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                    ["Legacy:RegisteredSources:0:Id"] = "v01",
                    ["Legacy:RegisteredSources:0:MediaRoot"] = Path.Combine(fixture, "media"),
                    ["Legacy:RegisteredSources:0:ArtifactRoot"] = Path.Combine(fixture, "artifacts"),
                    ["Legacy:SearchSourceId"] = "v01"
                });
            });
        });
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var bootstrap = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/security/antiforgery");
        return bootstrap.GetProperty("token").GetString()!;
    }

    private static async Task<CollectionDto> CreateCollectionAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/collections");
        request.Headers.Add("Origin", "http://127.0.0.1:5074");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(new { name = "Search fixture" });
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

    private static async Task<SearchResponse> PostSearchAsync(HttpClient client, string token, Guid collectionId, object body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/collections/{collectionId}/search");
        request.Headers.Add("Origin", "http://127.0.0.1:5074");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>())!;
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

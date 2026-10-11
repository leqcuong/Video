using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Application.Collections;

namespace ThesisDemo.IntegrationTests.Media;

public sealed class MediaSegmentApiTests
{
    [Fact]
    public async Task Imported_assets_expose_segment_browsing_head_and_range_media()
    {
        using var app = CreateFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5074") });
        var token = await GetTokenAsync(client);
        var collection = await CreateCollectionAsync(client, token);
        var preview = await PostImportAsync<ImportPreviewResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "preview" }, HttpStatusCode.Accepted);
        await PostImportAsync<ImportCommitResult>(client, token, collection.Id, new { registeredSourceId = "v01", action = "commit", previewId = preview.PreviewId }, HttpStatusCode.OK);

        var assets = await client.GetFromJsonAsync<PageResult<AssetDto>>($"/api/v1/collections/{collection.Id}/assets?pageSize=10");
        var asset = Assert.Single(assets!.Items, x => x.LegacyAssetId == "clip_a");

        var segments = await client.GetFromJsonAsync<PageResult<SegmentDto>>($"/api/v1/assets/{asset.Id}/segments?pageSize=10");
        Assert.Equal(2, segments!.Total);
        Assert.Equal("countdown begins", segments.Items[0].TranscriptText);
        Assert.Contains("caption", segments.Items[0].ModalityAvailability);

        using var head = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/assets/{asset.Id}/media");
        var headResponse = await client.SendAsync(head);
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal("video/mp4", headResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", headResponse.Headers.AcceptRanges);

        using var range = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{asset.Id}/media");
        range.Headers.Range = new RangeHeaderValue(0, 6);
        var rangeResponse = await client.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal("bytes", Assert.Single(rangeResponse.Headers.AcceptRanges));
        Assert.Equal("fixture", await rangeResponse.Content.ReadAsStringAsync());

        var missingThumbnail = await client.GetAsync($"/api/v1/segments/{segments.Items[0].Id}/thumbnail");
        Assert.Equal(HttpStatusCode.NotFound, missingThumbnail.StatusCode);

        using var invalidRange = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{asset.Id}/media");
        invalidRange.Headers.Range = new RangeHeaderValue(500, 700);
        var invalidResponse = await client.SendAsync(invalidRange);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalidResponse.StatusCode);
        Assert.Equal(16, invalidResponse.Content.Headers.ContentRange?.Length);

        using var suffix = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{asset.Id}/media");
        suffix.Headers.Range = new RangeHeaderValue(null, 4);
        var suffixResponse = await client.SendAsync(suffix);
        Assert.Equal(HttpStatusCode.PartialContent, suffixResponse.StatusCode);
        var completeMedia = await client.GetByteArrayAsync($"/api/v1/assets/{asset.Id}/media");
        Assert.Equal(completeMedia.TakeLast(4), await suffixResponse.Content.ReadAsByteArrayAsync());

        using var ifRange = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{asset.Id}/media");
        ifRange.Headers.Range = new RangeHeaderValue(0, 3);
        ifRange.Headers.TryAddWithoutValidation("If-Range", "\"different-file\"");
        var ifRangeResponse = await client.SendAsync(ifRange);
        Assert.Equal(HttpStatusCode.OK, ifRangeResponse.StatusCode);
        Assert.Equal(16, (await ifRangeResponse.Content.ReadAsByteArrayAsync()).Length);

        var missing = await client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/media");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("ASSET_NOT_FOUND", (await missing.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var repo = FindRepositoryRoot();
        var fixture = Path.Combine(repo, "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", "V01");
        var dbPath = Path.Combine(Path.GetTempPath(), $"thesis-media-{Guid.NewGuid():N}.db");
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

    private static async Task<CollectionDto> CreateCollectionAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/collections");
        request.Headers.Add("Origin", "http://127.0.0.1:5074");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(new { name = "Media fixture" });
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

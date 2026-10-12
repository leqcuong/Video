using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace ThesisDemo.BrowserTests;

public sealed class Phase123BrowserTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Library_search_and_real_video_playback(int width, int height)
    {
        var root = FindRepositoryRoot();
        var runDirectory = Path.Combine(Path.GetTempPath(), $"thesis-browser-{Guid.NewGuid():N}");
        var fixture = Path.Combine(runDirectory, "fixture");
        var source = Path.Combine(root, "web", "tests", "ThesisDemo.IntegrationTests", "Imports", "Fixtures", "V01");
        var video = Path.Combine(root, "web", "tests", "ThesisDemo.BrowserTests", "Fixtures", "clip_a.webm");
        CopyDirectory(source, fixture);
        File.Delete(Path.Combine(fixture, "media", "clip_a.mp4"));
        File.Delete(Path.Combine(fixture, "media", "clip_b.mp4"));
        File.Copy(video, Path.Combine(fixture, "media", "clip_a.webm"));
        File.Copy(video, Path.Combine(fixture, "media", "clip_b.webm"));

        var port = ReservePort();
        var url = $"http://127.0.0.1:{port}";
        var serverLog = new ConcurrentQueue<string>();
        using var server = StartServer(root, runDirectory, fixture, url, serverLog);
        try
        {
            await WaitForServerAsync(url, server);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--autoplay-policy=no-user-gesture-required"]
            });
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                AcceptDownloads = true
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(20000);
            try
            {
                var socket = page.WaitForWebSocketAsync(new PageWaitForWebSocketOptions { Timeout = 30000 });
                await page.GotoAsync($"{url}/library");
                await socket;
                await page.WaitForTimeoutAsync(700);
                await page.GetByRole(AriaRole.Button, new() { Name = "New collection" }).ClickAsync();
                await page.Locator(".list-group-item.active").WaitForAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
                await page.GetByText("Previewed 2 assets.").WaitForAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Commit", Exact = true }).ClickAsync();
                await page.GetByText("Imported 2; skipped 0; conflicts 0.").WaitForAsync();
                Assert.Equal(1, await page.GetByText("clip_a.webm").CountAsync());
                Assert.Equal(1, await page.GetByText("clip_b.webm").CountAsync());

                var searchSocket = page.WaitForWebSocketAsync(new PageWaitForWebSocketOptions { Timeout = 30000 });
                await page.GotoAsync($"{url}/search");
                await searchSocket;
                await page.WaitForTimeoutAsync(500);
                await page.Locator("#query").FillAsync("launch");
                Assert.Equal("launch", await page.Locator("#query").InputValueAsync());
                await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
                await page.Locator(".segment-list__item").First.WaitForAsync();
                Assert.Contains("clip_a", await page.Locator(".segment-list__item").First.InnerTextAsync());
                var player = page.Locator("video");
                await player.WaitForAsync();
                await page.WaitForFunctionAsync("() => document.querySelector('video')?.readyState >= 1");
                Assert.True(await player.EvaluateAsync<bool>("v => v.duration >= 20"));

                await page.EvaluateAsync("() => window.thesisVideoPlayer.seekToInterval(document.querySelector('video').id, 0.375, 1.125, true)");
                await page.WaitForFunctionAsync("() => { const v = document.querySelector('video'); return v.paused && v.currentTime >= 1.125; }", null,
                    new PageWaitForFunctionOptions { Timeout = 15000 });
                var stoppedAt = await player.EvaluateAsync<double>("v => v.currentTime");
                Assert.InRange(stoppedAt, 1.125, 1.6);

                Assert.True(await page.GetByText("Live reranking is not available.", new() { Exact = false }).IsVisibleAsync());
                Assert.False(await page.Locator("#mode-compare").CountAsync() > 0);
                Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth + 2"),
                    $"Horizontal overflow at {width}px");

                var screenshots = Path.Combine(root, "web", "artifacts", "verification", "screenshots");
                Directory.CreateDirectory(screenshots);
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(screenshots, $"search-{width}x{height}.png"), FullPage = true
                });
            }
            catch
            {
                var screenshots = Path.Combine(root, "web", "artifacts", "verification", "screenshots");
                Directory.CreateDirectory(screenshots);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(screenshots, $"failure-{width}x{height}.png"), FullPage = true });
                throw;
            }
            finally { await context.CloseAsync(); }
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            output.WriteLine($"Host exit code: {server.ExitCode}");
            foreach (var line in serverLog.TakeLast(30)) output.WriteLine(line);
            try { Directory.Delete(runDirectory, recursive: true); }
            catch (IOException) { }
        }
    }

    private static Process StartServer(string root, string tempDirectory, string fixture, string url, ConcurrentQueue<string> logs)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(root, "web", "src", "ThesisDemo.Api"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--no-build");
        info.ArgumentList.Add("--no-launch-profile");
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["Host__Url"] = url;
        info.Environment["Database__Provider"] = "Sqlite";
        info.Environment["ConnectionStrings__Sqlite"] = $"Data Source={Path.Combine(tempDirectory, "browser.db")}";
        info.Environment["Legacy__RegisteredSources__0__Id"] = "browser-fixture";
        info.Environment["Legacy__RegisteredSources__0__MediaRoot"] = Path.Combine(fixture, "media");
        info.Environment["Legacy__RegisteredSources__0__ArtifactRoot"] = Path.Combine(fixture, "artifacts");
        info.Environment["Legacy__SearchSourceId"] = "browser-fixture";
        var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to launch API host.");
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) logs.Enqueue(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) logs.Enqueue(args.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForServerAsync(string url, Process process)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (process.HasExited) throw new InvalidOperationException($"API host exited with {process.ExitCode}.");
            try
            {
                var response = await client.GetAsync($"{url}/library");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("API host did not start within 60 seconds.");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "web")) && Directory.Exists(Path.Combine(current.FullName, "specs"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

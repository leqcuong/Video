using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Domain.Library;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Storage;

public sealed class UploadValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class MediaUploadService(ManagedStorage storage, IDbContextFactory<ThesisDemoDbContext> factory, IConfiguration config)
{
    public const long MaxBytes = 256L * 1024 * 1024;
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime",
        [".webm"] = "video/webm", [".mkv"] = "video/x-matroska", [".avi"] = "video/x-msvideo"
    };

    public async Task<VideoAsset> UploadAsync(Guid collectionId, string name, Stream input, long length, CancellationToken ct)
    {
        name = Path.GetFileName(name.Replace('\\', '/'));
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length is < 1 or > 200 || !Types.TryGetValue(ext, out var contentType))
            throw new UploadValidationException("UNSUPPORTED_MEDIA", "Unsupported video file extension.");
        if (length <= 0 || length > MaxBytes) throw new UploadValidationException("UPLOAD_TOO_LARGE", "File is empty or exceeds 256 MiB.");
        await using (var db = await factory.CreateDbContextAsync(ct))
            if (!await db.Collections.AnyAsync(x => x.Id == collectionId, ct)) throw new KeyNotFoundException("Collection not found.");

        var uploadId = Guid.NewGuid();
        var relative = $"managed/uploads/{collectionId:N}/{uploadId:N}{ext}";
        var path = storage.Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            long copied = 0;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, FileOptions.Asynchronous))
            {
                var buffer = new byte[64 * 1024];
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    copied += count;
                    if (copied > MaxBytes) throw new UploadValidationException("UPLOAD_TOO_LARGE", "File exceeds 256 MiB.");
                    hasher.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            if (copied != length) throw new UploadValidationException("INVALID_MEDIA", "Uploaded file size does not match its declared length.");
            await ValidateSignatureAsync(path, ext, ct);
            var (duration, fps, width, height) = await ProbeAsync(path, ct);
            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var artifact = new ArtifactRecord(Guid.NewGuid(), "raw-media", relative, hash, copied, contentType,
                JsonSerializer.Serialize(new { origin = "upload", uploadedUtc = DateTimeOffset.UtcNow }));
            var asset = new VideoAsset(collectionId, uploadId.ToString("N"), name, hash, duration, fps, width, height);
            asset.AttachMedia(artifact.Id);
            await using var db = await factory.CreateDbContextAsync(ct);
            db.Artifacts.Add(artifact);
            db.VideoAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return asset;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static async Task ValidateSignatureAsync(string path, string extension, CancellationToken ct)
    {
        var buffer = new byte[16];
        await using var stream = File.OpenRead(path);
        if (await stream.ReadAsync(buffer, ct) < 12) throw new UploadValidationException("INVALID_MEDIA", "Video header is truncated.");
        var valid = extension switch
        {
            ".mp4" or ".m4v" or ".mov" => System.Text.Encoding.ASCII.GetString(buffer, 4, 4) == "ftyp",
            ".webm" or ".mkv" => buffer.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            ".avi" => System.Text.Encoding.ASCII.GetString(buffer, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(buffer, 8, 4) == "AVI ",
            _ => false
        };
        if (!valid) throw new UploadValidationException("INVALID_MEDIA", "File header does not match the video format.");
    }

    private async Task<(double Duration, double Fps, int Width, int Height)> ProbeAsync(string path, CancellationToken ct)
    {
        var executable = config["Processing:FfprobeExecutable"] ?? "ffprobe";
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate:format=duration", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("ffprobe failed to start.");
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var json = await stdout;
                await stderr;
                if (process.ExitCode != 0) throw new UploadValidationException("INVALID_MEDIA", "ffprobe could not read the video.");
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var stream = root.GetProperty("streams").EnumerateArray().First();
                var width = stream.GetProperty("width").GetInt32();
                var height = stream.GetProperty("height").GetInt32();
                var fraction = stream.GetProperty("avg_frame_rate").GetString()!.Split('/');
                var fps = fraction.Length == 2 ? double.Parse(fraction[0], System.Globalization.CultureInfo.InvariantCulture) /
                    double.Parse(fraction[1], System.Globalization.CultureInfo.InvariantCulture) : double.Parse(fraction[0], System.Globalization.CultureInfo.InvariantCulture);
                var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                if (!double.IsFinite(fps) || !double.IsFinite(duration) || fps <= 0 || duration <= 0 || width <= 0 || height <= 0)
                    throw new UploadValidationException("INVALID_MEDIA", "Video has invalid dimensions, duration or frame rate.");
                return (duration, fps, width, height);
            }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        }
        catch (System.ComponentModel.Win32Exception) { throw new UploadValidationException("PROBE_UNAVAILABLE", "ffprobe is not configured or installed."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UploadValidationException("PROBE_UNAVAILABLE", "Video probe timed out."); }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or DivideByZeroException)
        { throw new UploadValidationException("INVALID_MEDIA", "Video metadata could not be parsed."); }
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace ThesisDemo.Infrastructure.Storage;

public sealed record RegisteredLegacySource(string Id, string MediaRoot, string ArtifactRoot);

public sealed record LocalMediaFile(string RegisteredSourceId, string LegacyAssetId, string OriginalFileName,
    string FullPath, string RelativeArtifactPath, string Sha256, long ByteLength, string ContentType);

public sealed class LocalMediaStore
{
    private static readonly string[] MediaExtensions = [".mp4", ".mov", ".m4v", ".webm", ".avi", ".mkv"];
    private readonly IReadOnlyDictionary<string, RegisteredLegacySource> _sources;

    public LocalMediaStore(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Legacy:RegisteredSources").Get<RegisteredLegacySource[]>() ?? [];
        if (configured.Length == 0)
        {
            var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            configured =
            [
                new RegisteredLegacySource("full-rerun",
                    Path.Combine(repoRoot, "legacy-pipeline", "videos", "all_videos"),
                    Path.Combine(repoRoot, "legacy-pipeline", "outputs", "full_rerun"))
            ];
        }

        _sources = configured.ToDictionary(x => x.Id, x =>
        {
            if (string.IsNullOrWhiteSpace(x.Id)) throw new InvalidOperationException("Registered legacy source IDs are required.");
            return new RegisteredLegacySource(x.Id, NormalizeRoot(x.MediaRoot), NormalizeRoot(x.ArtifactRoot));
        }, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<RegisteredLegacySource> ListSources() => _sources.Values.OrderBy(x => x.Id).ToList();

    public RegisteredLegacySource GetSource(string id)
    {
        if (!_sources.TryGetValue(id, out var source)) throw new KeyNotFoundException($"Registered source '{id}' was not found.");
        return source;
    }

    public async Task<LocalMediaFile?> ResolveAsync(RegisteredLegacySource source, string legacyAssetId, string? sourceVideoPath, CancellationToken ct)
    {
        var candidates = CandidatePaths(source, legacyAssetId, sourceVideoPath).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var fullPath = Path.GetFullPath(candidate);
            EnsureUnderRoot(source.MediaRoot, fullPath, "Media file escapes the registered media root.");
            var info = new FileInfo(fullPath);
            await using var stream = info.OpenRead();
            var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            var name = Path.GetFileName(fullPath);
            return new LocalMediaFile(source.Id, legacyAssetId, name, fullPath,
                $"legacy/{source.Id}/media/{name}", sha256, info.Length, ContentTypeFor(name));
        }
        return null;
    }

    public async Task<FileHash> HashFileAsync(RegisteredLegacySource source, string fullPath, string artifactKind, CancellationToken ct)
    {
        var normalized = Path.GetFullPath(fullPath);
        EnsureUnderRoot(source.ArtifactRoot, normalized, "Artifact file escapes the registered artifact root.");
        var info = new FileInfo(normalized);
        await using var stream = info.OpenRead();
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        var relative = Path.GetRelativePath(source.ArtifactRoot, normalized).Replace('\\', '/');
        return new FileHash(artifactKind, $"legacy/{source.Id}/artifacts/{relative}", sha256, info.Length);
    }

    public string ResolveRegisteredArtifactPath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !string.Equals(parts[0], "legacy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Artifact path is not a registered legacy artifact.");
        var source = GetSource(parts[1]);
        var fileRelative = Path.Combine(parts.Skip(3).ToArray());
        var root = parts[2].Equals("media", StringComparison.OrdinalIgnoreCase)
            ? source.MediaRoot
            : parts[2].Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                ? source.ArtifactRoot
                : throw new InvalidOperationException("Unsupported registered artifact kind.");
        var fullPath = Path.GetFullPath(Path.Combine(root, fileRelative));
        EnsureUnderRoot(root, fullPath, "Artifact file escapes the registered root.");
        return fullPath;
    }

    private static IEnumerable<string> CandidatePaths(RegisteredLegacySource source, string legacyAssetId, string? sourceVideoPath)
    {
        if (!string.IsNullOrWhiteSpace(sourceVideoPath))
        {
            var fileName = Path.GetFileName(sourceVideoPath);
            if (!string.IsNullOrWhiteSpace(fileName)) yield return Path.Combine(source.MediaRoot, fileName);
        }
        foreach (var extension in MediaExtensions) yield return Path.Combine(source.MediaRoot, legacyAssetId + extension);
    }

    private static string NormalizeRoot(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        var root = Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), expanded));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureUnderRoot(string root, string fullPath, string message)
    {
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "legacy-pipeline")) && Directory.Exists(Path.Combine(current.FullName, "web")))
                return current.FullName;
            current = current.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream"
    };
}

public sealed record FileHash(string Kind, string RelativeArtifactPath, string Sha256, long ByteLength);

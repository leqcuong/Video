using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ThesisDemo.Application.Artifacts;

namespace ThesisDemo.Infrastructure.Storage;

public sealed class LocalArtifactStore(string root) : IArtifactStore
{
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    private readonly string _root = Path.GetFullPath(root);

    public async Task<Artifact> RegisterAsync(string kind, string relativePath, string sha256, long byteLength,
        string contentType, Provenance provenance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Split('/', '\\').Any(part => part is ".." or "." or ""))
            throw new ArgumentException("Artifact path must remain under the registered root.", nameof(relativePath));
        if (!Sha256Pattern.IsMatch(sha256)) throw new ArgumentException("SHA-256 must be lowercase hexadecimal.", nameof(sha256));
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Artifact escapes its root.");
        for (var current = fullPath; !current.Equals(_root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current)!)
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Artifact path contains a link.", nameof(relativePath));
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != byteLength) throw new InvalidDataException("Artifact size does not match.");
        await using var stream = info.OpenRead();
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(sha256))) throw new InvalidDataException("Artifact hash does not match.");
        return new Artifact(Guid.NewGuid(), kind, relativePath.Replace('\\', '/'), sha256, byteLength, contentType, provenance);
    }
}

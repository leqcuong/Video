using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ThesisDemo.Domain.Library;

namespace ThesisDemo.Infrastructure.Storage;

public sealed class ManagedStorage
{
    public string Root { get; }
    public ManagedStorage(IConfiguration configuration)
    {
        var configured = configuration["Storage:ManagedRoot"];
        Root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "managed-data")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
        Directory.CreateDirectory(Root);
    }

    public string Resolve(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length < 3 || parts[0] != "managed" || parts.Skip(1).Any(x => x is "" or "." or ".." || x.Contains('\\') || x.Contains(':')))
            throw new InvalidDataException("Invalid managed artifact path.");
        var path = Path.GetFullPath(Path.Combine(Root, Path.Combine(parts.Skip(1).ToArray())));
        if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artifact escapes managed storage.");
        for (var directory = Path.GetDirectoryName(path); directory is not null && directory.Length >= Root.Length; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Managed artifact contains a link.");
            if (directory.Equals(Root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    public async Task<ArtifactRecord> WriteAsync(string kind, Guid ownerId, string extension, byte[] bytes, string contentType, string provenance, CancellationToken ct)
    {
        if (extension is not ("json" or "csv" or "md")) throw new ArgumentException("Unsupported export format.");
        var id = Guid.NewGuid();
        var relative = $"managed/exports/{ownerId:N}/{id:N}.{extension}";
        var path = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactRecord(id, kind, relative, hash, bytes.LongLength, contentType, provenance);
    }
}

using System.Security.Cryptography;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.UnitTests;

public sealed class ArtifactTests
{
    [Fact]
    public async Task Store_checks_path_size_and_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"thesis-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var bytes = "trusted"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(root, "item.txt"), bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var provenance = new Provenance("revision", new Dictionary<string, string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        var store = new LocalArtifactStore(root);
        var artifact = await store.RegisterAsync("report", "item.txt", hash, bytes.Length, "text/plain", provenance);
        Assert.Equal(hash, artifact.Sha256);
        await Assert.ThrowsAsync<ArgumentException>(() => store.RegisterAsync("report", "../item.txt", hash, bytes.Length, "text/plain", provenance));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RegisterAsync("report", "item.txt", new string('0', 64), bytes.Length, "text/plain", provenance));
    }
}

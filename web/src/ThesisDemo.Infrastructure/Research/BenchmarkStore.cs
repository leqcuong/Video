using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThesisDemo.Application.Research.Benchmarks;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Infrastructure.Research;

public sealed record BenchmarkVersion(Guid Id, string Name, string Kind, Guid? ParentId,
    string Sha256, int RowCount, DateTimeOffset CreatedUtc, string DiagnosticsJson);

public sealed class BenchmarkStore(ManagedStorage storage)
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BenchmarkVersion> ImportAsync(string name, Stream source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ArgumentException("Invalid benchmark name.");
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidDataException("Benchmark exceeds 8 MiB.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        _ = new UTF8Encoding(false, true).GetString(bytes);
        // Preserve exactly the uploaded bytes, including whitespace and an optional UTF-8 BOM.
        var hasBom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
        using (var document = JsonDocument.Parse(bytes.AsMemory(hasBom ? 3 : 0)))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Benchmark must be a JSON array.");
            return await PersistAsync(name.Trim(), "raw", null, bytes,
                document.RootElement.GetArrayLength(), "{}", ct);
        }
    }

    public async Task<BenchmarkVersion> SanitizeAsync(Guid id, CancellationToken ct)
    {
        var (parent, raw) = await ReadAsync(id, ct);
        var result = BenchmarkTransforms.Sanitize(raw);
        var diagnostics = JsonSerializer.Serialize(new
        {
            result.InputCount, result.RetainedCount, result.Removed, method = "deterministic-basic-v1",
            note = "No index-leakage or semantic-noise filtering is claimed."
        }, JsonOptions);
        return await PersistAsync(parent.Name + " (clean)", "clean", id,
            Encoding.UTF8.GetBytes(result.Json), result.RetainedCount, diagnostics, ct);
    }

    public async Task<(BenchmarkVersion Training, BenchmarkVersion Evaluation)> SplitAsync(Guid id, double ratio, int seed, CancellationToken ct)
    {
        var (parent, raw) = await ReadAsync(id, ct);
        if (parent.Kind != "clean") throw new ArgumentException("Sanitize the benchmark before splitting.");
        var split = BenchmarkTransforms.Split(raw, ratio, seed);
        var diagnostics = JsonSerializer.Serialize(new
        {
            seed, trainRatio = ratio, split.TrainingCount, split.EvaluationCount,
            split.SharedNormalizedQueries, method = "query-group-sha256-v1"
        }, JsonOptions);
        var training = await PersistAsync(parent.Name + " (train)", "train", id,
            Encoding.UTF8.GetBytes(split.TrainingJson), split.TrainingCount, diagnostics, ct);
        var evaluation = await PersistAsync(parent.Name + " (evaluation)", "evaluation", id,
            Encoding.UTF8.GetBytes(split.EvaluationJson), split.EvaluationCount, diagnostics, ct);
        return (training, evaluation);
    }

    public IReadOnlyList<BenchmarkVersion> List()
    {
        var root = Path.Combine(storage.Root, "benchmarks");
        if (!Directory.Exists(root)) return [];
        var versions = new List<BenchmarkVersion>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id)) continue;
            var manifest = storage.Resolve($"managed/benchmarks/{id:N}/version.json");
            if (!File.Exists(manifest)) continue;
            versions.Add(JsonSerializer.Deserialize<BenchmarkVersion>(File.ReadAllText(manifest), JsonOptions)
                ?? throw new InvalidDataException("Invalid benchmark metadata."));
        }
        return versions.OrderByDescending(x => x.CreatedUtc).ToList();
    }

    public async Task<(BenchmarkVersion Version, string Json)> ReadAsync(Guid id, CancellationToken ct)
    {
        var manifest = storage.Resolve($"managed/benchmarks/{id:N}/version.json");
        if (!File.Exists(manifest)) throw new KeyNotFoundException("Benchmark not found.");
        var version = JsonSerializer.Deserialize<BenchmarkVersion>(await File.ReadAllTextAsync(manifest, ct), JsonOptions)
            ?? throw new InvalidDataException("Invalid benchmark metadata.");
        if (version.Id != id) throw new InvalidDataException("Benchmark metadata mismatch.");
        var bytes = await File.ReadAllBytesAsync(storage.Resolve($"managed/benchmarks/{id:N}/rows.json"), ct);
        if (Hash(bytes) != version.Sha256) throw new InvalidDataException("Benchmark content changed after registration.");
        return (version, new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
    }

    private async Task<BenchmarkVersion> PersistAsync(string name, string kind, Guid? parent,
        byte[] rows, int count, string diagnostics, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var version = new BenchmarkVersion(id, name, kind, parent, Hash(rows), count,
            DateTimeOffset.UtcNow, diagnostics);
        var directory = Path.Combine(storage.Root, "benchmarks", id.ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(storage.Resolve($"managed/benchmarks/{id:N}/rows.json"), rows, ct);
        await File.WriteAllTextAsync(storage.Resolve($"managed/benchmarks/{id:N}/version.json"),
            JsonSerializer.Serialize(version, JsonOptions), ct);
        return version;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

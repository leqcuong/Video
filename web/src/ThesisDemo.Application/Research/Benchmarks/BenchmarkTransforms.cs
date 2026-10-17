using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThesisDemo.Application.Research.Benchmarks;

public sealed record BenchmarkCleanResult(string Json, int InputCount, int RetainedCount,
    IReadOnlyDictionary<string, int> Removed);
public sealed record BenchmarkSplitResult(string TrainingJson, string EvaluationJson,
    int TrainingCount, int EvaluationCount, int SharedNormalizedQueries);

public static partial class BenchmarkTransforms
{
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    public static string Normalize(string value) =>
        string.Join(' ', Tokens().Matches(value).Select(m => m.Value.ToLowerInvariant()));

    public static BenchmarkCleanResult Sanitize(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Benchmark must be a JSON array.");
        var retained = new List<JsonElement>();
        var removed = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            total++;
            var reason = ValidateRow(row);
            if (reason is null)
            {
                var key = Normalize(row.GetProperty("query").GetString()!);
                if (!seen.Add(key)) reason = "duplicate_query";
            }
            if (reason is not null)
            {
                removed[reason] = removed.GetValueOrDefault(reason) + 1;
                continue;
            }
            retained.Add(row.Clone());
        }
        return new BenchmarkCleanResult(JsonSerializer.Serialize(retained), total, retained.Count, removed);
    }

    public static BenchmarkSplitResult Split(string cleaned, double ratio, int seed)
    {
        if (!double.IsFinite(ratio) || ratio is < 0.1 or > 0.9) throw new ArgumentOutOfRangeException(nameof(ratio));
        using var doc = JsonDocument.Parse(cleaned);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Benchmark must be a JSON array.");
        var rows = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
        if (rows.Length < 4 || rows.Any(x => ValidateRow(x) is not null))
            throw new InvalidDataException("Split requires at least four valid benchmark rows.");

        // Assign normalized query groups together to prevent duplicate leakage.
        var groups = rows.Select((row, index) => (row, index))
            .GroupBy(x => Normalize(x.row.GetProperty("query").GetString()!), StringComparer.Ordinal)
            .Select(g => new { Key = g.Key, Indices = g.Select(x => x.index).ToArray(),
                Order = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{g.Key}"))) })
            .OrderBy(x => x.Order, StringComparer.Ordinal).ToArray();
        if (groups.Length < 2) throw new InvalidDataException("Cannot split fewer than two distinct queries.");
        var goal = Math.Clamp((int)Math.Round(rows.Length * ratio), 1, rows.Length - 1);
        var trainIndices = new HashSet<int>();
        foreach (var group in groups.Take(groups.Length - 1))
        {
            if (trainIndices.Count >= goal) break;
            foreach (var index in group.Indices) trainIndices.Add(index);
        }
        if (trainIndices.Count == 0) foreach (var index in groups[0].Indices) trainIndices.Add(index);
        var train = rows.Where((_, i) => trainIndices.Contains(i)).ToArray();
        var eval = rows.Where((_, i) => !trainIndices.Contains(i)).ToArray();
        var overlap = train.Select(x => Normalize(x.GetProperty("query").GetString()!))
            .Intersect(eval.Select(x => Normalize(x.GetProperty("query").GetString()!)), StringComparer.Ordinal).Count();
        return new BenchmarkSplitResult(JsonSerializer.Serialize(train), JsonSerializer.Serialize(eval), train.Length, eval.Length, overlap);
    }

    private static string? ValidateRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("query", out var query) ||
            query.ValueKind != JsonValueKind.String || Normalize(query.GetString() ?? "").Length == 0)
            return "missing_query";
        var spans = row.TryGetProperty("expected", out var expected) && expected.ValueKind == JsonValueKind.Array
            ? expected.EnumerateArray().ToArray() : new[] { row };
        if (spans.Length == 0 || spans.All(x => x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("asset_id", out _)))
            return "missing_expected";
        foreach (var span in spans)
        {
            if (span.ValueKind != JsonValueKind.Object ||
                !span.TryGetProperty("asset_id", out var asset) || asset.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(asset.GetString()) ||
                !span.TryGetProperty("start_sec", out var start) || start.ValueKind != JsonValueKind.Number ||
                !span.TryGetProperty("end_sec", out var end) || end.ValueKind != JsonValueKind.Number ||
                !double.IsFinite(start.GetDouble()) || !double.IsFinite(end.GetDouble()) ||
                start.GetDouble() < 0 || end.GetDouble() <= start.GetDouble()) return "invalid_span";
        }
        return null;
    }
}

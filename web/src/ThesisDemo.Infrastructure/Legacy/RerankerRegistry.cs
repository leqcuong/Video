using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ThesisDemo.Infrastructure.Legacy;

public sealed record RerankerModelInfo(
    string ModelId,
    string ModelType,
    string FeatureSchemaHash,
    string Sha256,
    IReadOnlyList<string> FeatureNames,
    IReadOnlyDictionary<string, JsonElement> TrainingStats);

public sealed class RerankerRegistry
{
    public static readonly string[] ExpectedFeatureNames =
    [
        "bm25_norm",
        "semantic_norm",
        "hybrid_score",
        "query_caption_overlap",
        "query_transcript_overlap",
        "query_ocr_overlap",
        "segment_duration_sec",
        "rank_inverse"
    ];

    public async Task<RerankerModelInfo> ValidateAsync(string modelId, string modelPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        await using var stream = File.OpenRead(modelPath);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Reranker model must be a JSON object.");
        var modelType = RequiredString(root, "model_type");
        if (modelType != "logreg_numpy") throw new InvalidDataException($"Unsupported reranker model_type: {modelType}.");
        var features = RequiredStringArray(root, "feature_names");
        if (!features.SequenceEqual(ExpectedFeatureNames, StringComparer.Ordinal))
            throw new InvalidDataException("Reranker feature schema does not match the legacy adapter.");
        ValidateNumericArray(root, "weights", features.Count);
        ValidateNumericArray(root, "feature_mean", features.Count);
        ValidateNumericArray(root, "feature_std", features.Count);
        _ = RequiredDouble(root, "bias");
        var schemaHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", features)))).ToLowerInvariant();
        var stats = root.TryGetProperty("training_stats", out var trainingStats) && trainingStats.ValueKind == JsonValueKind.Object
            ? trainingStats.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : new Dictionary<string, JsonElement>();
        return new RerankerModelInfo(modelId, modelType, schemaHash, sha256, features, stats);
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Reranker model missing string property '{property}'.");
        return value.GetString()!;
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Reranker model missing array property '{property}'.");
        return value.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
    }

    private static void ValidateNumericArray(JsonElement root, string property, int expectedCount)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Reranker model missing numeric array '{property}'.");
        var values = value.EnumerateArray().ToList();
        if (values.Count != expectedCount) throw new InvalidDataException($"{property} length must be {expectedCount}.");
        foreach (var item in values)
            if (!item.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new InvalidDataException($"{property} contains a non-finite value.");
    }

    private static double RequiredDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvalidDataException($"Reranker model missing finite numeric property '{property}'.");
        return number;
    }
}

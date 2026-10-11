using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ThesisDemo.Application.Processing;

public sealed record ProcessingSettings(
    double SceneThreshold = 27, int MinSceneLenFrames = 15, int FixedWindowSec = 10,
    bool ForceFixedWindow = false, bool RequestedAsr = false, bool RequestedOcr = false,
    string? CaptionModelId = null, string? EmbeddingModelId = null);

public sealed record ProcessingRequest(Guid[] AssetIds, string Mode = "local", string? PresetId = null,
    ProcessingSettings? Settings = null, string PublishPolicy = "manual");

public sealed record ProcessingSnapshot(Guid[] AssetIds, ProcessingSettings Settings, string PublishPolicy,
    string CaptionModelId, string EmbeddingModelId);

public static class ProcessingValidation
{
    public static ProcessingSnapshot Validate(ProcessingRequest request, string captionModel, string embeddingModel)
    {
        if (request.AssetIds is not { Length: > 0 and <= 100 } || request.AssetIds.Any(x => x == Guid.Empty) ||
            request.AssetIds.Distinct().Count() != request.AssetIds.Length)
            throw new ArgumentException("Provide 1-100 unique asset IDs.");
        if (request.Mode != "local") throw new ArgumentException("Only local processing is currently supported; Colab handoff is not implemented.");
        if (request.PresetId is not null and not "thesis-default") throw new ArgumentException("Unsupported preset.");
        if (request.PublishPolicy is not ("manual" or "onSuccess")) throw new ArgumentException("Unsupported publish policy.");
        var settings = request.Settings ?? new ProcessingSettings();
        if (!double.IsFinite(settings.SceneThreshold) || settings.SceneThreshold <= 0 ||
            settings.MinSceneLenFrames < 1 || settings.FixedWindowSec < 1 || settings.FixedWindowSec > 3600)
            throw new ArgumentException("Invalid processing parameters.");
        if (settings.CaptionModelId is not null && settings.CaptionModelId != captionModel ||
            settings.EmbeddingModelId is not null && settings.EmbeddingModelId != embeddingModel)
            throw new ArgumentException("Only configured model choices are allowed.");
        return new ProcessingSnapshot(request.AssetIds.Order().ToArray(), settings, request.PublishPolicy, captionModel, embeddingModel);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Serialize<T>(T data) => JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

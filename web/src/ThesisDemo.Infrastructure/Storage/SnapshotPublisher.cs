using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Processing;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Search;
using ThesisDemo.Infrastructure.Legacy;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Storage;

public sealed class SnapshotPublisher(ManagedStorage storage)
{
    public async Task<IndexSnapshot> ComposeAsync(ThesisDemoDbContext db, Guid runId,
        ProcessingSnapshot config, PipelineOutcome outcome, IReadOnlyList<VideoAsset> succeeded,
        bool allSucceeded, CancellationToken ct)
    {
        if (outcome.EmbeddingBackend != "sentence-transformers" || outcome.EmbeddingModel != config.EmbeddingModelId)
            throw new InvalidDataException("Embedding backend/model not verified. Hash-fallback cannot be published as a semantic index.");

        var rows = new List<JsonObject>();
        var membership = new Dictionary<Guid, Guid>();
        var indexPath = Path.Combine(outcome.OutputRoot, "index", "combined_index.json");
        var produced = await ReadRowsAsync(indexPath, ct);
        var acceptedIds = succeeded.Select(x => x.Id.ToString("N")).ToHashSet(StringComparer.Ordinal);
        if (produced.Any(x => !acceptedIds.Contains(Text(x, "asset_id"))))
            throw new InvalidDataException("Index references an unsuccessful or unknown asset.");

        var expected = new HashSet<string>(StringComparer.Ordinal);
        var segmentBounds = new Dictionary<string, (double Start, double End)>(StringComparer.Ordinal);
        foreach (var asset in succeeded)
        {
            var stem = asset.Id.ToString("N");
            var segmentsPath = Path.Combine(outcome.OutputRoot, stem, "segments.json");
            var bytes = await File.ReadAllBytesAsync(segmentsPath, ct);
            using var segmentJson = JsonDocument.Parse(bytes);
            if (segmentJson.RootElement.ValueKind != JsonValueKind.Array || segmentJson.RootElement.GetArrayLength() == 0)
                throw new InvalidDataException("Successful assets require nonempty segment sets.");
            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var attempt = await db.ProcessingAssetAttempts.SingleAsync(x => x.RunId == runId && x.AssetId == asset.Id, ct);
            var set = new SegmentSet(asset.Id, runId, attempt.Id,
                ProcessingValidation.Hash(ProcessingValidation.Serialize(config)), sourceHash);
            db.SegmentSets.Add(set);
            membership.Add(asset.Id, set.Id);
            foreach (var item in segmentJson.RootElement.EnumerateArray())
            {
                var assetId = item.GetProperty("asset_id").GetString();
                var segmentId = item.GetProperty("segment_id").GetString()!;
                var start = item.GetProperty("start_sec").GetDouble();
                var end = item.GetProperty("end_sec").GetDouble();
                if (assetId != stem || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start ||
                    end > asset.DurationSec + 1 || !expected.Add(stem + ":" + segmentId))
                    throw new InvalidDataException("Segment identity, bounds or uniqueness mismatch.");
                segmentBounds.Add(stem + ":" + segmentId, (start, end));
                var segment = new VideoSegment(set.Id, segmentId, start, end, item.GetProperty("caption").GetString()!);
                segment.AddEvidenceText(OptionalText(item, "transcript_text"), OptionalText(item, "ocr_text"), OptionalText(item, "summary"));
                db.VideoSegments.Add(segment);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dimension = 0;
        foreach (var row in produced)
        {
            var key = Text(row, "asset_id") + ":" + Text(row, "segment_id");
            if (!expected.Contains(key) || !seen.Add(key)) throw new InvalidDataException("Index/segment identity mismatch.");
            var start = Number(row, "start_sec");
            var end = Number(row, "end_sec");
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
                throw new InvalidDataException("Invalid index timestamps.");
            var bounds = segmentBounds[key];
            if (Math.Abs(start - bounds.Start) > 0.001 || Math.Abs(end - bounds.End) > 0.001)
                throw new InvalidDataException("Index timestamps disagree with the validated segment set.");
            if (row["embedding"] is not JsonArray vector || vector.Count == 0)
                throw new InvalidDataException("Missing embedding vector.");
            if (dimension == 0) dimension = vector.Count;
            if (vector.Count != dimension || vector.Any(x => x is null || !double.IsFinite(x!.GetValue<double>())))
                throw new InvalidDataException("Inconsistent or non-finite embedding vectors.");
            row["source_video"] = "";
            rows.Add(row);
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Index is missing segment rows.");

        var collection = await db.Collections.SingleAsync(x => x.Id == succeeded[0].CollectionId, ct);
        if (collection.ActiveIndexId is Guid priorId)
        {
            var previous = await db.IndexSnapshots.SingleAsync(x => x.Id == priorId, ct);
            if (previous.State != SnapshotState.Ready || previous.EmbeddingModel != config.EmbeddingModelId || previous.Dimension != dimension)
                throw new InvalidDataException("Active index embedding space is incompatible with this run.");
            var priorArtifact = await db.Artifacts.SingleAsync(x => x.Id == previous.ArtifactId, ct);
            var old = await ReadRowsAsync(storage.Resolve(priorArtifact.RelativePath), ct);
            var olderMembers = await db.IndexMembers.Where(x => x.IndexId == priorId && !membership.Keys.Contains(x.AssetId)).ToListAsync(ct);
            var oldAssets = await db.VideoAssets.Where(x => olderMembers.Select(m => m.AssetId).Contains(x.Id)).ToListAsync(ct);
            var oldIds = oldAssets.SelectMany(x => new[] { x.LegacyAssetId, x.Id.ToString("N") }).ToHashSet(StringComparer.Ordinal);
            var oldRows = old.Where(x => oldIds.Contains(Text(x, "asset_id"))).ToList();
            if (oldRows.Count == 0 && olderMembers.Count != 0) throw new InvalidDataException("Prior snapshot membership has no index rows.");
            rows.InsertRange(0, oldRows);
            foreach (var member in olderMembers) membership.Add(member.AssetId, member.SegmentSetId);
        }
        if (rows.Count == 0) throw new InvalidDataException("No index rows to publish.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (rows.Any(x => !keys.Add(Text(x, "asset_id") + ":" + Text(x, "segment_id"))))
            throw new InvalidDataException("Duplicate index row across snapshots.");

        var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows));
        var relative = $"managed/processing/{runId:N}/published-index.json";
        var destination = storage.Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, data, ct);
        var provenance = ProcessingValidation.Serialize(new
        {
            schemaVersion = 1, runId, config.EmbeddingModelId, embeddingBackend = outcome.EmbeddingBackend,
            revision = "runtime-model-unpinned", dimension, rowCount = rows.Count,
            configuration = config, generatedUtc = DateTimeOffset.UtcNow,
            indexSha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
        });
        var artifact = new ArtifactRecord(Guid.NewGuid(), "live-index", relative,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), data.Length,
            "application/json", provenance);
        db.Artifacts.Add(artifact);
        var snapshot = new IndexSnapshot(collection.Id, config.EmbeddingModelId,
            "runtime-model-unpinned", dimension, rows.Count,
            ProcessingValidation.Hash(ProcessingValidation.Serialize(config)));
        snapshot.BeginValidation();
        snapshot.Publish(artifact.Id);
        db.IndexSnapshots.Add(snapshot);
        foreach (var entry in membership) db.IndexMembers.Add(new IndexMember(snapshot.Id, entry.Key, entry.Value));
        if (allSucceeded && config.PublishPolicy == "onSuccess") collection.ActivateIndex(snapshot);
        return snapshot;
    }

    private static async Task<List<JsonObject>> ReadRowsAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        var node = await JsonNode.ParseAsync(input, cancellationToken: ct);
        if (node is not JsonArray array || array.Count == 0 || array.Any(x => x is not JsonObject))
            throw new InvalidDataException("Index must contain nonempty JSON objects.");
        return array.Select(x => (JsonObject)x!).ToList();
    }

    private static string Text(JsonObject row, string name) => row[name]?.GetValue<string>() ?? throw new InvalidDataException($"Missing {name}.");
    private static double Number(JsonObject row, string name) => row[name]?.GetValue<double>() ?? throw new InvalidDataException($"Missing {name}.");
    private static string? OptionalText(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

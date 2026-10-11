using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Collections;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Domain.Search;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;

namespace ThesisDemo.Infrastructure.Legacy;

public sealed class LegacyCorpusImporter(IDbContextFactory<ThesisDemoDbContext> factory, LocalMediaStore mediaStore) : ILegacyCorpusImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public async Task<ImportPreviewResult> PreviewAsync(Guid collectionId, string registeredSourceId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Collections.AnyAsync(x => x.Id == collectionId, ct)) throw new KeyNotFoundException("Collection was not found.");
        var source = mediaStore.GetSource(registeredSourceId);
        var rows = await ReadRowsAsync(source, ct);
        return ToPreview(registeredSourceId, rows);
    }

    public async Task<ImportCommitResult> CommitAsync(Guid collectionId, string registeredSourceId, string? previewId, CancellationToken ct)
    {
        var source = mediaStore.GetSource(registeredSourceId);
        var rows = await ReadRowsAsync(source, ct);
        var preview = ToPreview(registeredSourceId, rows);
        if (!string.IsNullOrWhiteSpace(previewId) && !SamePreviewId(preview.PreviewId, previewId))
            throw new InvalidOperationException("Preview ID does not match the current source contents.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Collections.AnyAsync(x => x.Id == collectionId, ct)) throw new KeyNotFoundException("Collection was not found.");

        var imported = 0;
        var skipped = 0;
        var conflicted = 0;
        var segmentsImported = 0;
        var diagnostics = new List<string>(preview.Diagnostics);
        var validRows = rows.Where(x => x.IsValid).OrderBy(x => x.LegacyAssetId, StringComparer.OrdinalIgnoreCase).ToList();
        if (validRows.Count == 0) return new ImportCommitResult(preview.PreviewId, registeredSourceId, 0, 0, 0, 0, diagnostics);

        var run = new ProcessingRun(collectionId, RunMode.Local, JsonSerializer.Serialize(new { import = registeredSourceId }, JsonOptions),
            preview.PreviewId, preview.PreviewId);
        run.TransitionTo(RunState.Queued);
        run.TransitionTo(RunState.Running);
        run.TransitionTo(RunState.Validating);
        db.ProcessingRuns.Add(run);

        foreach (var row in validRows)
        {
            var existing = await db.VideoAssets.SingleOrDefaultAsync(x => x.CollectionId == collectionId && x.LegacyAssetId == row.LegacyAssetId, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.Sha256, row.Media!.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    conflicted++;
                    diagnostics.Add($"{row.LegacyAssetId}: existing asset hash differs from registered source.");
                }
                else skipped++;
                continue;
            }

            var mediaArtifact = await AddArtifactIfMissingAsync(db, "raw-media", row.Media!.RelativeArtifactPath,
                row.Media.Sha256, row.Media.ByteLength, row.Media.ContentType, ProvenanceJson(row), ct);
            await AddArtifactIfMissingAsync(db, "legacy-asset-metadata", row.MetadataHash!.RelativeArtifactPath,
                row.MetadataHash.Sha256, row.MetadataHash.ByteLength, "application/json", ProvenanceJson(row), ct);
            await AddArtifactIfMissingAsync(db, "legacy-segments", row.SegmentsHash!.RelativeArtifactPath,
                row.SegmentsHash.Sha256, row.SegmentsHash.ByteLength, "application/json", ProvenanceJson(row), ct);

            var asset = new VideoAsset(collectionId, row.LegacyAssetId, row.Media.OriginalFileName, row.Media.Sha256,
                row.DurationSec!.Value, row.Fps!.Value, row.Width!.Value, row.Height!.Value);
            asset.AttachMedia(mediaArtifact.Id);
            asset.SetState(AssetState.Ready);
            db.VideoAssets.Add(asset);

            var attempt = new ProcessingAssetAttempt(run.Id, asset.Id, Guid.NewGuid(), row.AssetDirectory);
            db.ProcessingAssetAttempts.Add(attempt);
            var segmentSet = new SegmentSet(asset.Id, run.Id, attempt.Id, preview.PreviewId, row.SegmentsHash.Sha256);
            db.SegmentSets.Add(segmentSet);
            foreach (var importedSegment in row.Segments)
            {
                var segment = new VideoSegment(segmentSet.Id, importedSegment.LegacySegmentId,
                    importedSegment.StartSec, importedSegment.EndSec, importedSegment.Caption);
                segment.AddEvidenceText(importedSegment.TranscriptText, importedSegment.OcrText, importedSegment.Summary);
                db.VideoSegments.Add(segment);
                segmentsImported++;
            }
            imported++;
        }

        run.TransitionTo(conflicted > 0 && imported == 0 ? RunState.Partial : RunState.Succeeded);
        await db.SaveChangesAsync(ct);
        return new ImportCommitResult(preview.PreviewId, registeredSourceId, imported, skipped, conflicted, segmentsImported, diagnostics);
    }

    private async Task<IReadOnlyList<ImportRow>> ReadRowsAsync(RegisteredLegacySource source, CancellationToken ct)
    {
        var rows = new List<ImportRow>();
        foreach (var metadataPath in Directory.EnumerateFiles(source.ArtifactRoot, "asset_metadata.json", SearchOption.AllDirectories))
        {
            var assetDirectory = Path.GetDirectoryName(metadataPath)!;
            var segmentsPath = Path.Combine(assetDirectory, "segments.json");
            var diagnostics = new List<string>();
            try
            {
                var metadataHash = await mediaStore.HashFileAsync(source, metadataPath, "legacy-asset-metadata", ct);
                if (!File.Exists(segmentsPath)) diagnostics.Add("segments.json is missing.");
                var segmentsHash = File.Exists(segmentsPath)
                    ? await mediaStore.HashFileAsync(source, segmentsPath, "legacy-segments", ct)
                    : null;
                var metadata = await LoadMetadataAsync(metadataPath, ct);
                if (string.IsNullOrWhiteSpace(metadata.LegacyAssetId)) diagnostics.Add("asset_id is missing.");
                if (!metadata.DurationSec.HasValue || metadata.DurationSec <= 0) diagnostics.Add("duration_sec must be positive.");
                if (!metadata.Fps.HasValue || metadata.Fps <= 0) diagnostics.Add("fps must be positive.");
                if (!metadata.Width.HasValue || metadata.Width <= 0 || !metadata.Height.HasValue || metadata.Height <= 0) diagnostics.Add("width and height must be positive.");

                var segments = File.Exists(segmentsPath) ? await LoadSegmentsAsync(segmentsPath, metadata.LegacyAssetId, diagnostics, ct) : [];
                var media = string.IsNullOrWhiteSpace(metadata.LegacyAssetId)
                    ? null
                    : await mediaStore.ResolveAsync(source, metadata.LegacyAssetId, metadata.SourceVideo, ct);
                if (media is null) diagnostics.Add("media file is missing or outside the registered media root.");

                rows.Add(new ImportRow(source.Id, assetDirectory, metadata.LegacyAssetId ?? Path.GetFileName(assetDirectory),
                    metadata.SourceVideo, metadata.DurationSec, metadata.Fps, metadata.Width, metadata.Height,
                    metadataHash, segmentsHash, media, segments, diagnostics));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                rows.Add(new ImportRow(source.Id, assetDirectory, Path.GetFileName(assetDirectory), null, null, null, null, null,
                    null, null, null, [], [ex.Message]));
            }
        }
        return rows;
    }

    private static ImportPreviewResult ToPreview(string registeredSourceId, IReadOnlyList<ImportRow> rows)
    {
        var assets = rows.OrderBy(x => x.LegacyAssetId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ImportAssetPreview(x.LegacyAssetId, x.Media?.OriginalFileName ?? Path.GetFileName(x.SourceVideo ?? x.LegacyAssetId),
                x.IsValid, x.Media is not null, x.Segments.Count, x.Media?.Sha256, x.Diagnostics))
            .ToList();
        var diagnostics = rows.SelectMany(x => x.Diagnostics.Select(d => $"{x.LegacyAssetId}: {d}")).ToList();
        diagnostics.AddRange(rows.Where(x => x.Media is not null)
            .GroupBy(x => x.Media!.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => $"filename collision: {x.Key} maps to {string.Join(", ", x.Select(r => r.LegacyAssetId).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))}."));
        var previewId = BuildPreviewId(registeredSourceId, rows);
        return new ImportPreviewResult(previewId, registeredSourceId, rows.Count, rows.Count(x => x.IsValid),
            rows.Count(x => !x.IsValid), rows.Sum(x => x.Segments.Count), assets, diagnostics);
    }

    private static bool SamePreviewId(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string BuildPreviewId(string sourceId, IReadOnlyList<ImportRow> rows)
    {
        var builder = new StringBuilder(sourceId);
        foreach (var row in rows.OrderBy(x => x.LegacyAssetId, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|').Append(row.LegacyAssetId)
                .Append(':').Append(row.Media?.Sha256)
                .Append(':').Append(row.MetadataHash?.Sha256)
                .Append(':').Append(row.SegmentsHash?.Sha256)
                .Append(':').Append(row.Segments.Count);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static async Task<ArtifactRecord> AddArtifactIfMissingAsync(ThesisDemoDbContext db, string kind, string relativePath,
        string sha256, long byteLength, string contentType, string provenanceJson, CancellationToken ct)
    {
        var existing = await db.Artifacts.SingleOrDefaultAsync(x => x.RelativePath == relativePath, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.Sha256, sha256, StringComparison.OrdinalIgnoreCase) || existing.ByteLength != byteLength)
                throw new InvalidDataException($"Artifact path '{relativePath}' was already registered with different bytes.");
            return existing;
        }
        var artifact = new ArtifactRecord(Guid.NewGuid(), kind, relativePath, sha256, byteLength, contentType, provenanceJson);
        db.Artifacts.Add(artifact);
        return artifact;
    }

    private static string ProvenanceJson(ImportRow row) => JsonSerializer.Serialize(new
    {
        source = row.RegisteredSourceId,
        legacyAssetId = row.LegacyAssetId,
        importedUtc = DateTimeOffset.UtcNow,
        sourceHashes = new Dictionary<string, string?>
        {
            ["media"] = row.Media?.Sha256,
            ["asset_metadata"] = row.MetadataHash?.Sha256,
            ["segments"] = row.SegmentsHash?.Sha256
        }
    }, JsonOptions);

    private static async Task<LegacyMetadata> LoadMetadataAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        return new LegacyMetadata(
            ReadString(root, "asset_id"),
            ReadString(root, "source_video"),
            ReadDouble(root, "duration_sec"),
            ReadDouble(root, "fps"),
            ReadInt(root, "width"),
            ReadInt(root, "height"));
    }

    private static async Task<IReadOnlyList<ImportedSegment>> LoadSegmentsAsync(string path, string? expectedAssetId, List<string> diagnostics, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("segments.json must be a JSON array.");
            return [];
        }
        var result = new List<ImportedSegment>();
        var ordinal = 0;
        foreach (var element in document.RootElement.EnumerateArray())
        {
            ordinal++;
            var legacySegmentId = ReadString(element, "segment_id") ?? $"seg_{ordinal:0000}";
            var assetId = ReadString(element, "asset_id");
            var start = ReadDouble(element, "start_sec");
            var end = ReadDouble(element, "end_sec");
            var caption = ReadString(element, "caption");
            if (!string.IsNullOrWhiteSpace(expectedAssetId) && !string.IsNullOrWhiteSpace(assetId) &&
                !string.Equals(expectedAssetId, assetId, StringComparison.Ordinal))
                diagnostics.Add($"{legacySegmentId}: asset_id does not match metadata.");
            if (!start.HasValue || !end.HasValue || start < 0 || end <= start)
            {
                diagnostics.Add($"{legacySegmentId}: invalid time bounds.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(caption))
            {
                diagnostics.Add($"{legacySegmentId}: caption is required.");
                continue;
            }
            result.Add(new ImportedSegment(legacySegmentId, start.Value, end.Value, caption,
                ReadString(element, "transcript_text"), ReadString(element, "ocr_text"), ReadString(element, "summary")));
        }
        if (result.Count == 0) diagnostics.Add("segments.json contains no valid segments.");
        return result;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private sealed record LegacyMetadata(string? LegacyAssetId, string? SourceVideo, double? DurationSec, double? Fps, int? Width, int? Height);

    private sealed record ImportedSegment(string LegacySegmentId, double StartSec, double EndSec, string Caption,
        string? TranscriptText, string? OcrText, string? Summary);

    private sealed record ImportRow(string RegisteredSourceId, string AssetDirectory, string LegacyAssetId, string? SourceVideo,
        double? DurationSec, double? Fps, int? Width, int? Height, FileHash? MetadataHash, FileHash? SegmentsHash,
        LocalMediaFile? Media, IReadOnlyList<ImportedSegment> Segments, IReadOnlyList<string> Diagnostics)
    {
        public bool IsValid => Diagnostics.Count == 0 && Media is not null && MetadataHash is not null &&
            SegmentsHash is not null && Segments.Count > 0 && DurationSec.HasValue && Fps.HasValue &&
            Width.HasValue && Height.HasValue;
    }
}

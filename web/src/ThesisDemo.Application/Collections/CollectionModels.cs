using ThesisDemo.Domain.Library;

namespace ThesisDemo.Application.Collections;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record CollectionDto(Guid Id, string Name, bool Archived, Guid? ActiveIndexId,
    Guid? ActiveRerankerId, int AssetCount, DateTimeOffset CreatedUtc, string Version);

public sealed record AssetDto(Guid Id, Guid CollectionId, string OriginalFileName, string LegacyAssetId,
    string State, string MediaAvailability, double DurationSec, double Fps, int Width, int Height,
    Guid? ProcessingRunId, Guid? ActiveSegmentSetId);

public sealed record SegmentDto(Guid Id, Guid AssetId, Guid SegmentSetId, string LegacySegmentId,
    double StartSec, double EndSec, string Caption, string? TranscriptText, string? OcrText,
    string ModalityAvailability, string? ThumbnailUrl, string? MediaUrl);

public sealed record CreateCollectionCommand(string Name);
public sealed record UpdateCollectionCommand(string? Name, bool? Archived, string? IfMatch);
public sealed record ListAssetsQuery(int Page = 1, int PageSize = 25, AssetState? State = null, string? Name = null);

public enum ImportAction { Preview, Commit }
public sealed record ImportCollectionCommand(string RegisteredSourceId, ImportAction Action, string? PreviewId);

public sealed record ImportAssetPreview(string LegacyAssetId, string OriginalFileName, bool IsValid,
    bool MediaExists, int SegmentCount, string? Sha256, IReadOnlyList<string> Diagnostics);

public sealed record ImportPreviewResult(string PreviewId, string RegisteredSourceId, int AssetsDiscovered,
    int AssetsValid, int AssetsInvalid, int SegmentsDiscovered, IReadOnlyList<ImportAssetPreview> Assets,
    IReadOnlyList<string> Diagnostics);

public sealed record ImportCommitResult(string PreviewId, string RegisteredSourceId, int AssetsImported,
    int AssetsSkipped, int AssetsConflicted, int SegmentsImported, IReadOnlyList<string> Diagnostics);

public interface ICollectionStore
{
    Task<PageResult<CollectionDto>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<CollectionDto?> GetAsync(Guid id, CancellationToken ct);
    Task<CollectionDto> CreateAsync(CreateCollectionCommand command, CancellationToken ct);
    Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionCommand command, CancellationToken ct);
    Task<PageResult<AssetDto>> ListAssetsAsync(Guid collectionId, ListAssetsQuery query, CancellationToken ct);
}

public interface ILegacyCorpusImporter
{
    Task<ImportPreviewResult> PreviewAsync(Guid collectionId, string registeredSourceId, CancellationToken ct);
    Task<ImportCommitResult> CommitAsync(Guid collectionId, string registeredSourceId, string? previewId, CancellationToken ct);
}

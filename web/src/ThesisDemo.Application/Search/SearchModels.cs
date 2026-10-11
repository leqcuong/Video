namespace ThesisDemo.Application.Search;

public sealed record SearchRequest(
    string Query,
    Guid? IndexId = null,
    int TopK = 10,
    int CandidateK = 200,
    double HybridAlpha = 0.6,
    SearchMode Mode = SearchMode.Baseline,
    string? ModelId = null,
    IReadOnlyList<Guid>? AssetIds = null);

public enum SearchMode { Baseline, Reranked, Compare }

public sealed record SearchResponse(
    string RequestId,
    string? IndexId,
    string? ModelId,
    SearchEffectiveConfigDto EffectiveConfig,
    long ElapsedMs,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SearchResultDto>? BaselineResults,
    IReadOnlyList<SearchResultDto>? RerankedResults);

public sealed record SearchEffectiveConfigDto(int TopK, int CandidateK, double HybridAlpha);

public sealed record SearchResultDto(
    int Rank,
    Guid SegmentId,
    Guid AssetId,
    string LegacyAssetId,
    string LegacySegmentId,
    double StartSec,
    double EndSec,
    string Caption,
    string? TranscriptText,
    string? OcrText,
    double Score,
    string ScoreKind,
    string MediaUrl,
    string? ThumbnailUrl,
    int? BaselineRank = null);

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(Guid collectionId, SearchRequest request, CancellationToken ct);
}

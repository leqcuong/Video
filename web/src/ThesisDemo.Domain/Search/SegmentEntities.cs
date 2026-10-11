using ThesisDemo.Domain.Library;

namespace ThesisDemo.Domain.Search;

public sealed class SegmentSet : VersionedEntity
{
    private SegmentSet() { }
    public SegmentSet(Guid assetId, Guid runId, Guid attemptId, string configHash, string sourceHash)
    {
        if (assetId == Guid.Empty || runId == Guid.Empty || attemptId == Guid.Empty) throw new ArgumentException("Asset, run and attempt are required.");
        AssetId = assetId; RunId = runId; AttemptId = attemptId; ConfigHash = configHash; SourceHash = sourceHash;
    }
    public Guid AssetId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid AttemptId { get; private set; }
    public string ConfigHash { get; private set; } = "";
    public string SourceHash { get; private set; } = "";
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
}

public sealed class VideoSegment : VersionedEntity
{
    private VideoSegment() { }
    public VideoSegment(Guid segmentSetId, string legacySegmentId, double startSec, double endSec, string caption)
    {
        if (segmentSetId == Guid.Empty || string.IsNullOrWhiteSpace(legacySegmentId)) throw new ArgumentException("Segment set and legacy ID are required.");
        if (!double.IsFinite(startSec) || !double.IsFinite(endSec) || startSec < 0 || endSec <= startSec) throw new ArgumentOutOfRangeException(nameof(startSec));
        if (string.IsNullOrWhiteSpace(caption)) throw new ArgumentException("Caption is required.");
        SegmentSetId = segmentSetId; LegacySegmentId = legacySegmentId; StartSec = startSec; EndSec = endSec; Caption = caption.Trim();
    }
    public Guid SegmentSetId { get; private set; }
    public string LegacySegmentId { get; private set; } = "";
    public double StartSec { get; private set; }
    public double EndSec { get; private set; }
    public string Caption { get; private set; } = "";
    public string? TranscriptText { get; private set; }
    public string? OcrText { get; private set; }
    public string? Summary { get; private set; }
    public Guid? ThumbnailArtifactId { get; private set; }
}

public sealed class IndexMember
{
    private IndexMember() { }
    public IndexMember(Guid indexId, Guid assetId, Guid segmentSetId)
    { IndexId = indexId; AssetId = assetId; SegmentSetId = segmentSetId; }
    public Guid IndexId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid SegmentSetId { get; private set; }
}

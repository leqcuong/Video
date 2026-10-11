using ThesisDemo.Domain.Library;

namespace ThesisDemo.Domain.Search;

public enum SnapshotState { Building, Validating, Ready, Rejected }
public sealed class IndexSnapshot : VersionedEntity
{
    private IndexSnapshot() { }
    public IndexSnapshot(Guid collectionId, string embeddingModel, string revision, int dimension, long rowCount, string configHash)
    {
        if (string.IsNullOrWhiteSpace(embeddingModel) || string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("A known model and revision are required.");
        if (dimension <= 0 || rowCount < 0) throw new ArgumentOutOfRangeException(nameof(dimension));
        CollectionId = collectionId; EmbeddingModel = embeddingModel; Revision = revision; Dimension = dimension; RowCount = rowCount; ConfigHash = configHash;
    }
    public Guid CollectionId { get; private set; }
    public SnapshotState State { get; private set; } = SnapshotState.Building;
    public Guid? ArtifactId { get; private set; }
    public string EmbeddingModel { get; private set; } = "";
    public string Revision { get; private set; } = "";
    public int Dimension { get; private set; }
    public long RowCount { get; private set; }
    public string ConfigHash { get; private set; } = "";
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public void BeginValidation() { if (State != SnapshotState.Building) throw new InvalidOperationException(); State = SnapshotState.Validating; Touch(); }
    public void Publish(Guid artifactId) { if (State != SnapshotState.Validating || artifactId == Guid.Empty || RowCount == 0) throw new InvalidOperationException(); ArtifactId = artifactId; State = SnapshotState.Ready; Touch(); }
    public void Reject() { if (State is SnapshotState.Ready or SnapshotState.Rejected) throw new InvalidOperationException(); State = SnapshotState.Rejected; Touch(); }
}

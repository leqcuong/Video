namespace ThesisDemo.Domain.Library;

public abstract class VersionedEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid Version { get; private set; } = Guid.NewGuid();
    public void Touch() => Version = Guid.NewGuid();
}

public sealed class Collection : VersionedEntity
{
    private Collection() { }
    public Collection(string name) => Rename(name);
    public string Name { get; private set; } = "";
    public Guid? ActiveIndexId { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public void Rename(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(name));
        Name = name; Touch();
    }
    public void ActivateIndex(ThesisDemo.Domain.Search.IndexSnapshot snapshot)
    {
        if (snapshot.CollectionId != Id || snapshot.State != ThesisDemo.Domain.Search.SnapshotState.Ready)
            throw new InvalidOperationException("Only a ready snapshot from this collection may be activated.");
        ActiveIndexId = snapshot.Id; Touch();
    }
}

public enum AssetState { Imported, Queued, Processing, Ready, Failed, MissingMedia }

public sealed class VideoAsset : VersionedEntity
{
    private static readonly IReadOnlyDictionary<AssetState, AssetState[]> Allowed = new Dictionary<AssetState, AssetState[]>
    {
        [AssetState.Imported] = [AssetState.Queued, AssetState.Ready, AssetState.MissingMedia],
        [AssetState.Queued] = [AssetState.Processing, AssetState.Failed],
        [AssetState.Processing] = [AssetState.Ready, AssetState.Failed],
        [AssetState.Ready] = [AssetState.Queued, AssetState.MissingMedia],
        [AssetState.Failed] = [AssetState.Queued],
        [AssetState.MissingMedia] = [AssetState.Ready]
    };
    private VideoAsset() { }
    public VideoAsset(Guid collectionId, string legacyAssetId, string originalFileName, string sha256,
        double durationSec, double fps, int width, int height)
    {
        if (collectionId == Guid.Empty) throw new ArgumentException("Collection is required.");
        if (string.IsNullOrWhiteSpace(legacyAssetId)) throw new ArgumentException("Legacy asset ID is required.");
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Contains('/') || originalFileName.Contains('\\'))
            throw new ArgumentException("Only an original filename is allowed.");
        if (!double.IsFinite(durationSec) || !double.IsFinite(fps) || durationSec <= 0 || fps <= 0 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(durationSec));
        if (sha256.Length != 64 || !sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) throw new ArgumentException("SHA-256 is required.", nameof(sha256));
        CollectionId = collectionId; LegacyAssetId = legacyAssetId; OriginalFileName = originalFileName;
        Sha256 = sha256; DurationSec = durationSec; Fps = fps; Width = width; Height = height;
    }
    public Guid CollectionId { get; private set; }
    public string LegacyAssetId { get; private set; } = "";
    public string OriginalFileName { get; private set; } = "";
    public Guid? MediaArtifactId { get; private set; }
    public string Sha256 { get; private set; } = "";
    public double DurationSec { get; private set; }
    public double Fps { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public AssetState State { get; private set; } = AssetState.Imported;
    public void AttachMedia(Guid mediaArtifactId)
    {
        if (mediaArtifactId == Guid.Empty) throw new ArgumentException("Media artifact is required.", nameof(mediaArtifactId));
        MediaArtifactId = mediaArtifactId; Touch();
    }
    public void SetState(AssetState state)
    {
        if (!Allowed.TryGetValue(State, out var next) || !next.Contains(state)) throw new InvalidOperationException($"Cannot transition {State} to {state}.");
        State = state; Touch();
    }
}

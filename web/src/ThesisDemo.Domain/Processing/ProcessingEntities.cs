using ThesisDemo.Domain.Library;

namespace ThesisDemo.Domain.Processing;

public enum RunMode { Local, Colab }
public enum RunState { Draft, Queued, Running, WaitingExternal, Validating, CancelRequested, Succeeded, Partial, Failed, Interrupted, Canceled }

public sealed class ProcessingRun : VersionedEntity
{
    private static readonly IReadOnlyDictionary<RunState, RunState[]> Allowed = new Dictionary<RunState, RunState[]>
    {
        [RunState.Draft] = [RunState.Queued], [RunState.Queued] = [RunState.Running, RunState.WaitingExternal, RunState.Canceled],
        [RunState.WaitingExternal] = [RunState.Validating, RunState.Canceled],
        [RunState.Running] = [RunState.Validating, RunState.CancelRequested, RunState.Failed, RunState.Interrupted],
        [RunState.CancelRequested] = [RunState.Canceled],
        [RunState.Validating] = [RunState.Succeeded, RunState.Partial, RunState.Failed, RunState.Canceled]
    };
    private ProcessingRun() { }
    public ProcessingRun(Guid collectionId, RunMode mode, string configJson, string configHash, string inputSnapshotHash)
    { CollectionId = collectionId; Mode = mode; ConfigJson = configJson; ConfigHash = configHash; InputSnapshotHash = inputSnapshotHash; }
    public Guid CollectionId { get; private set; }
    public RunMode Mode { get; private set; }
    public RunState State { get; private set; } = RunState.Draft;
    public string ConfigJson { get; private set; } = "{}";
    public string ConfigHash { get; private set; } = "";
    public string InputSnapshotHash { get; private set; } = "";
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; private set; }
    public DateTimeOffset? EndedUtc { get; private set; }
    public void TransitionTo(RunState next)
    {
        if (!Allowed.TryGetValue(State, out var values) || !values.Contains(next)) throw new InvalidOperationException($"Cannot transition {State} to {next}.");
        State = next; if (next == RunState.Running) StartedUtc = DateTimeOffset.UtcNow;
        if (next is RunState.Succeeded or RunState.Partial or RunState.Failed or RunState.Interrupted or RunState.Canceled) EndedUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class ProcessingAssetAttempt : VersionedEntity
{
    private ProcessingAssetAttempt() { }
    public ProcessingAssetAttempt(Guid runId, Guid assetId, Guid attemptToken, string outputRoot)
    { RunId = runId; AssetId = assetId; AttemptToken = attemptToken; OutputRoot = outputRoot; }
    public Guid RunId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid AttemptToken { get; private set; }
    public RunState State { get; private set; } = RunState.Queued;
    public string OutputRoot { get; private set; } = "";
    public string? ErrorCode { get; private set; }
}

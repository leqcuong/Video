using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Domain.Search;

namespace ThesisDemo.UnitTests;

public sealed class DomainInvariantTests
{
    [Fact] public void Collection_rejects_blank_name() => Assert.Throws<ArgumentOutOfRangeException>(() => new Collection(" "));
    [Fact] public void Asset_rejects_path_as_filename() => Assert.Throws<ArgumentException>(() => new VideoAsset(Guid.NewGuid(), "x", "../x.mp4", new string('a',64), 1, 25, 10, 10));
    [Fact] public void Run_enforces_transition_graph()
    {
        var run = new ProcessingRun(Guid.NewGuid(), RunMode.Local, "{}", "c", "i");
        Assert.Throws<InvalidOperationException>(() => run.TransitionTo(RunState.Succeeded));
        run.TransitionTo(RunState.Queued); run.TransitionTo(RunState.Running); run.TransitionTo(RunState.Validating); run.TransitionTo(RunState.Succeeded);
        Assert.NotNull(run.EndedUtc);
    }
    [Fact] public void Snapshot_is_immutable_after_publish()
    {
        var snapshot = new IndexSnapshot(Guid.NewGuid(), "model", "rev", 384, 3, "hash");
        snapshot.BeginValidation(); snapshot.Publish(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(snapshot.Reject);
    }
    [Fact] public void Segment_rejects_non_finite_or_empty_evidence()
    {
        var set = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoSegment(set, "id", double.NaN, 2, "caption"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoSegment(set, "id", 2, 2, "caption"));
        Assert.Throws<ArgumentException>(() => new VideoSegment(set, "id", 0, 2, " "));
    }
    [Fact] public void Activation_requires_ready_snapshot_from_same_collection()
    {
        var collection = new Collection("demo");
        var foreign = new IndexSnapshot(Guid.NewGuid(), "model", "rev", 384, 1, "hash");
        foreign.BeginValidation(); foreign.Publish(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => collection.ActivateIndex(foreign));
        var own = new IndexSnapshot(collection.Id, "model", "rev", 384, 1, "hash");
        Assert.Throws<InvalidOperationException>(() => collection.ActivateIndex(own));
        own.BeginValidation(); own.Publish(Guid.NewGuid()); collection.ActivateIndex(own);
        Assert.Equal(own.Id, collection.ActiveIndexId);
    }
    [Fact] public void Asset_state_graph_rejects_skips()
    {
        var asset = new VideoAsset(Guid.NewGuid(), "legacy", "clip.mp4", new string('a', 64), 5, 25, 100, 100);
        Assert.Throws<InvalidOperationException>(() => asset.SetState(AssetState.Processing));
        asset.SetState(AssetState.Queued); asset.SetState(AssetState.Processing); asset.SetState(AssetState.Ready);
        Assert.Throws<InvalidOperationException>(() => asset.SetState(AssetState.Imported));
    }
}

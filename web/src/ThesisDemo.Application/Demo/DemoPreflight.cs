namespace ThesisDemo.Application.Demo;

public sealed record DemoCheck(string Name, string Status, string Detail);

public sealed record DemoQuery(string Purpose, string Query, string Evidence);

public sealed record DemoPreflight(IReadOnlyList<DemoCheck> Checks,
    IReadOnlyList<DemoQuery> Queries, DateTimeOffset CheckedUtc)
{
    public bool ReadyForOfflineSearch => Checks
        .Where(x => x.Name is "Active index" or "Media files")
        .All(x => x.Status == "ready") && Checks.Any(x => x.Name == "Active index");
}

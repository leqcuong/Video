namespace ThesisDemo.Infrastructure.Research;

public sealed class RecordedEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string SummaryJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public DateTimeOffset ImportedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecordedQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluationId { get; set; }
    public int Ordinal { get; set; }
    public string Query { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int? BaselineRank { get; set; }
    public int? RerankedRank { get; set; }
    public double BaselineTop1Iou { get; set; }
    public double RerankedTop1Iou { get; set; }
    public string ExpectedJson { get; set; } = "[]";
    public string RetrievedJson { get; set; } = "{}";
}

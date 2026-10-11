namespace ThesisDemo.Application.Search;

public static class SearchValidation
{
    public static void Validate(SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length > 2000)
            throw new ArgumentException("Query length must be 1-2000 characters.", nameof(request));
        if (request.TopK is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request.TopK), "topK must be 1-100.");
        if (request.CandidateK < request.TopK || request.CandidateK > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.CandidateK), "candidateK must be topK-1000.");
        if (!double.IsFinite(request.HybridAlpha) || request.HybridAlpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(request.HybridAlpha), "hybridAlpha must be 0-1.");
        if (request.Mode is SearchMode.Reranked or SearchMode.Compare && string.IsNullOrWhiteSpace(request.ModelId))
            throw new ArgumentException("modelId is required for reranked or compare search.");
    }
}

using ThesisDemo.Application.Search;

namespace ThesisDemo.UnitTests;

public sealed class SearchValidationTests
{
    [Theory]
    [InlineData(0.0, 1, 1)]
    [InlineData(0.6, 10, 200)]
    [InlineData(1.0, 100, 1000)]
    public void Boundary_search_parameters_are_valid(double alpha, int topK, int candidateK)
    {
        SearchValidation.Validate(new SearchRequest("  rocket  ", TopK: topK, CandidateK: candidateK, HybridAlpha: alpha));
    }

    [Theory]
    [InlineData(0, 1, 0.6)]
    [InlineData(101, 101, 0.6)]
    [InlineData(2, 1, 0.6)]
    [InlineData(1, 1001, 0.6)]
    [InlineData(1, 1, -0.01)]
    [InlineData(1, 1, 1.01)]
    [InlineData(1, 1, double.NaN)]
    [InlineData(1, 1, double.PositiveInfinity)]
    public void Invalid_search_parameters_are_rejected(int topK, int candidateK, double alpha)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SearchValidation.Validate(new SearchRequest("rocket", TopK: topK, CandidateK: candidateK, HybridAlpha: alpha)));
    }

    [Fact]
    public void Empty_and_overlong_queries_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SearchValidation.Validate(new SearchRequest(" \t ")));
        Assert.Throws<ArgumentException>(() => SearchValidation.Validate(new SearchRequest(new string('x', 2001))));
    }

    [Theory]
    [InlineData(SearchMode.Compare)]
    [InlineData(SearchMode.Reranked)]
    public void Nonbaseline_modes_require_a_model(SearchMode mode)
    {
        Assert.Throws<ArgumentException>(() => SearchValidation.Validate(new SearchRequest("rocket", Mode: mode)));
        SearchValidation.Validate(new SearchRequest("rocket", Mode: mode, ModelId: "lexical-fallback"));
    }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexExtractorValidationRegressionTests
{
    [Fact]
    public void ExtractorRejectsMalformedCompositeFiltersWithValidationException()
    {
        var malformed = new FilterExpression(FilterExpressionKind.And)
        {
            Children = null!,
        };

        Assert.Throws<FilterValidationException>(() =>
            FilterIndexExtractor.Extract(typeof(IndexValidationSubject), malformed));
    }

    [Fact]
    public void IndexAddRejectsMalformedCompositeFiltersWithValidationException()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(IndexValidationSubject));
        var malformed = new FilterExpression(FilterExpressionKind.And)
        {
            Children = null!,
        };

        Assert.Throws<FilterValidationException>(() => index.Add("bad", malformed));
    }

    [Fact]
    public void ExtractorRejectsInvalidNonEqualityCompareWithValidationException()
    {
        var invalid = FilterExpression.Compare(
            nameof(IndexValidationSubject.Name),
            FilterOperator.GreaterThan,
            FilterValue.From("A"));

        Assert.Throws<FilterValidationException>(() =>
            FilterIndexExtractor.Extract(typeof(IndexValidationSubject), invalid));
    }

    [Fact]
    public void ExtractorReturnsNullForValidNonEqualityCompare()
    {
        var unindexed = FilterExpression.Compare(
            nameof(IndexValidationSubject.Id),
            FilterOperator.GreaterThan,
            FilterValue.From(0L));

        Assert.Null(FilterIndexExtractor.Extract(typeof(IndexValidationSubject), unindexed));
    }

    private sealed record IndexValidationSubject(int Id, string Name = "") : IFilterSubject;
}

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

    private sealed record IndexValidationSubject(int Id) : IFilterSubject;
}

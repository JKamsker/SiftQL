using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class FilterFingerprintRegressionTests
{
    [Fact]
    public void EqualNumericFilterKeysHaveEqualFingerprints()
    {
        FilterExpressionKey positive = FilterExpressionFingerprint.CreateKey(
            FilterExpression.Compare("Score", FilterOperator.Equal, FilterValue.From(0.0D)));
        FilterExpressionKey negative = FilterExpressionFingerprint.CreateKey(
            FilterExpression.Compare("Score", FilterOperator.Equal, FilterValue.From(-0.0D)));

        Assert.True(positive.Equals(negative));
        Assert.Equal(positive.ToString(), negative.ToString());
    }

    [Fact]
    public void EqualDecimalProjectionKeysHaveEqualFingerprints()
    {
        ProjectionExpressionKey first = ProjectionExpressionFingerprint.CreateKey(
            ProjectionArgument(FilterValue.From(1.10M)));
        ProjectionExpressionKey second = ProjectionExpressionFingerprint.CreateKey(
            ProjectionArgument(FilterValue.From(1.100M)));

        Assert.True(first.Equals(second));
        Assert.Equal(first.ToString(), second.ToString());
    }

    private static EventProjectionExpression ProjectionArgument(FilterValue value) =>
        EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "window",
                "result",
                [new EventProjectionArgument("amount", value)]),
        ]);
}

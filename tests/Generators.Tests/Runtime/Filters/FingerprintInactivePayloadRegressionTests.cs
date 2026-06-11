using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class FingerprintInactivePayloadRegressionTests
{
    [Fact]
    public void ExistsFilterKeysIgnoreInactivePayloads()
    {
        FilterExpressionKey first = FilterExpressionFingerprint.CreateKey(
            FilterExpression.Exists("Region") with
            {
                Value = FilterValue.From(1L),
            });
        FilterExpressionKey second = FilterExpressionFingerprint.CreateKey(
            FilterExpression.Exists("Region") with
            {
                Value = FilterValue.From(2L),
            });

        Assert.Equal(first.ToString(), second.ToString());
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void SourceFieldProjectionKeysIgnoreInactiveValuePayloads()
    {
        ProjectionExpressionKey first = ProjectionExpressionFingerprint.CreateKey(
            ProjectionWithSourceArgument(FilterValue.From(1L)));
        ProjectionExpressionKey second = ProjectionExpressionFingerprint.CreateKey(
            ProjectionWithSourceArgument(FilterValue.From(2L)));

        Assert.Equal(first.ToString(), second.ToString());
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    private static EventProjectionExpression ProjectionWithSourceArgument(FilterValue inactiveValue) =>
        EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.include",
                "result",
                new EventProjectionArgument
                {
                    Name = "itemId",
                    Kind = EventProjectionArgumentKind.SourceField,
                    SourcePath = "ItemId",
                    Value = inactiveValue,
                }),
        ]);
}

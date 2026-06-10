using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterQueryRepresentationRegressionTests
{
    [Fact]
    public void FormatRejectsTimestampValuesInsteadOfEmittingEmptyString()
    {
        FilterExpression filter = FilterExpression.Compare(
            "OccurredAt",
            FilterOperator.GreaterThanOrEqual,
            FilterValue.From(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));

        FilterQueryException ex = Assert.Throws<FilterQueryException>(() =>
            FilterQuery.Format(filter));

        Assert.Contains(nameof(FilterValueKind.Timestamp), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id in~ [1]")]
    [InlineData("score between~ [1, 2]")]
    [InlineData("score >~ 1")]
    [InlineData("score <=~ 10")]
    public void ParseRejectsIgnoreCaseMarkerWhereItHasNoMeaning(string query)
    {
        Assert.Throws<FilterQueryException>(() => FilterQuery.Parse(query));
    }
}

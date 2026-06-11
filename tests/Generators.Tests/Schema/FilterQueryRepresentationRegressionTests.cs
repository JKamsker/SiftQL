using SiftQL.Expressions;
using SiftQL.Projected;
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
    [InlineData(1.0D)]
    [InlineData(-0.0D)]
    public void FormatPreservesWholeNumberDoubleValueKind(double value)
    {
        FilterExpression filter = FilterExpression.Compare(
            "Score",
            FilterOperator.Equal,
            FilterValue.From(value));

        FilterExpression reparsed = FilterQuery.Parse(FilterQuery.Format(filter));

        Assert.Equal(FilterExpression.ContentSignature(filter), FilterExpression.ContentSignature(reparsed));
        Assert.Equal(FilterValueKind.Number, reparsed.Value!.Kind);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FormatRejectsNonFiniteNumbersInsteadOfEmittingUnparseableText(double value)
    {
        FilterExpression filter = FilterExpression.Compare(
            "Score",
            FilterOperator.Equal,
            FilterValue.From(value));

        FilterQueryException ex = Assert.Throws<FilterQueryException>(() =>
            FilterQuery.Format(filter));

        Assert.Contains(nameof(FilterValueKind.Number), ex.Message, StringComparison.Ordinal);
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

    [Fact]
    public void FormatRoundTripsProjectedFieldPaths()
    {
        FilterExpression filter = FilterExpression.Compare(
            ProjectedEventPaths.Field("ItemId"),
            FilterOperator.Equal,
            FilterValue.From(100L));

        FilterExpression reparsed = FilterQuery.Parse(FilterQuery.Format(filter));

        Assert.Equal(FilterExpression.ContentSignature(filter), FilterExpression.ContentSignature(reparsed));
    }
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterQueryParserTests
{
    [Fact]
    public void ParsesEqualityAndConjunction()
    {
        FilterExpression filter = FilterQuery.Parse("region == \"EU\" and total > 100");

        Assert.Equal(FilterExpressionKind.And, filter.Kind);
        Assert.Equal(2, filter.Children.Length);
    }

    [Fact]
    public void ParsesStringOperators()
    {
        Assert.Equal(FilterOperator.StringContains, FilterQuery.Parse("name contains \"x\"").Operator);
        Assert.Equal(FilterOperator.StringStartsWith, FilterQuery.Parse("name startswith \"x\"").Operator);
        Assert.Equal(FilterOperator.StringEndsWith, FilterQuery.Parse("name endswith \"x\"").Operator);
    }

    [Fact]
    public void ParsesInList()
    {
        FilterExpression filter = FilterQuery.Parse("id in [1, 2, 3]");

        Assert.Equal(FilterExpressionKind.In, filter.Kind);
        Assert.Equal(3, filter.Values.Length);
    }

    [Fact]
    public void ParsesNotWithParenthesesAndOr()
    {
        FilterExpression filter = FilterQuery.Parse("not (a == 1 or b == 2)");

        Assert.Equal(FilterExpressionKind.Not, filter.Kind);
        Assert.Equal(FilterExpressionKind.Or, filter.Children[0].Kind);
    }

    [Fact]
    public void ParsesValueLiterals()
    {
        Assert.Equal(FilterValueKind.Boolean, FilterQuery.Parse("active == true").Value!.Kind);
        Assert.Equal(FilterValueKind.Null, FilterQuery.Parse("name == null").Value!.Kind);
        Assert.Equal(FilterValueKind.Number, FilterQuery.Parse("ratio < 1.5").Value!.Kind);
        Assert.Equal(FilterValueKind.Integer, FilterQuery.Parse("count >= 3").Value!.Kind);
    }

    [Fact]
    public void RoundTripsThroughFormat()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")),
            FilterExpression.Or(
                FilterExpression.Compare("total", FilterOperator.GreaterThan, FilterValue.From(100L)),
                FilterExpression.StringContains("tags", FilterValue.From("vip"))));

        string text = FilterQuery.Format(filter);
        FilterExpression reparsed = FilterQuery.Parse(text);

        Assert.Equal(
            FilterExpression.ContentSignature(filter),
            FilterExpression.ContentSignature(reparsed));
    }

    [Fact]
    public void RoundTripsCaseInsensitiveAndExtendedValueKinds()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.StringContains("name", FilterValue.From("vip"), ignoreCase: true),
            FilterExpression.Compare("big", FilterOperator.Equal, FilterValue.From(ulong.MaxValue)),
            FilterExpression.Compare("price", FilterOperator.GreaterThan, FilterValue.From(9.95m)),
            FilterExpression.Compare(
                "id",
                FilterOperator.Equal,
                FilterValue.From(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"))));

        string text = FilterQuery.Format(filter);
        FilterExpression reparsed = FilterQuery.Parse(text);

        // ContentSignature encodes IgnoreCase and the exact value kind, so an equal
        // signature proves the case flag and UnsignedInteger/Decimal/Guid kinds
        // survived the Format -> Parse round-trip.
        Assert.Equal(
            FilterExpression.ContentSignature(filter),
            FilterExpression.ContentSignature(reparsed));
    }

    [Fact]
    public void RoundTripsIntegralDecimalKindThroughFormat()
    {
        var decimalOne = new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 1m };
        FilterExpression filter = FilterExpression.Compare(
            "price",
            FilterOperator.Equal,
            decimalOne);

        FilterExpression reparsed = FilterQuery.Parse(FilterQuery.Format(filter));

        Assert.Equal(FilterValueKind.Decimal, reparsed.Value!.Kind);
        Assert.Equal(
            FilterExpression.ContentSignature(filter),
            FilterExpression.ContentSignature(reparsed));
    }

    [Fact]
    public void ThrowsOnIncompleteInput()
    {
        Assert.Throws<FilterQueryException>(() => FilterQuery.Parse("region =="));
        Assert.Throws<FilterQueryException>(() => FilterQuery.Parse("region @@ 1"));
    }

    [Fact]
    public void ParsedFilterCompilesAndMatches()
    {
        FilterExpression filter = FilterQuery.Parse("Region == \"EU\" and Total > 100");
        var kernel = FilterCompiler.Compile(typeof(OrderEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new OrderEvent("EU", 150)));
        Assert.False(kernel.Matches(new OrderEvent("EU", 50)));
        Assert.False(kernel.Matches(new OrderEvent("US", 150)));
    }

    private sealed record OrderEvent(string Region, double Total) : IFilterSubject;
}

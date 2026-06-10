using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionSimplifyTests
{
    private static FilterExpression Eq(string field, long value) =>
        FilterExpression.Compare(field, FilterOperator.Equal, FilterValue.From(value));

    [Fact]
    public void RemovesDoubleNegation()
    {
        FilterExpression x = Eq("a", 1);
        FilterExpression notNot = FilterExpression.Not(FilterExpression.Not(x));

        Assert.Equal(
            FilterExpression.ContentSignature(x),
            FilterExpression.ContentSignature(FilterExpression.Simplify(notNot)));
    }

    [Fact]
    public void ContradictoryEqualitiesAreUnsatisfiable()
    {
        FilterExpression filter = FilterExpression.And(Eq("id", 1), Eq("id", 2));

        Assert.True(FilterExpression.IsAlwaysFalse(filter));
        Assert.False(FilterExpression.IsSatisfiable(filter));
    }

    [Fact]
    public void EqualAndNotEqualSameValueIsUnsatisfiable()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("s", FilterOperator.Equal, FilterValue.From("a")),
            FilterExpression.Compare("s", FilterOperator.NotEqual, FilterValue.From("a")));

        Assert.True(FilterExpression.IsAlwaysFalse(filter));
    }

    [Fact]
    public void ComplementaryLiteralsInOrAreTautology()
    {
        FilterExpression x = Eq("a", 1);
        FilterExpression filter = FilterExpression.Or(x, FilterExpression.Not(x));

        Assert.True(FilterExpression.IsAlwaysTrue(filter));
    }

    [Fact]
    public void SatisfiableFilterStaysSatisfiable()
    {
        FilterExpression filter = FilterExpression.And(
            Eq("id", 1),
            FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")));

        Assert.True(FilterExpression.IsSatisfiable(filter));
        Assert.False(FilterExpression.IsAlwaysFalse(filter));
        Assert.False(FilterExpression.IsAlwaysTrue(filter));
    }

    [Fact]
    public void RedundantAnyInAndIsDropped()
    {
        FilterExpression x = Eq("a", 1);
        FilterExpression withAny = new FilterExpression(FilterExpressionKind.And)
        {
            Children = [x, FilterExpression.Any],
        };

        Assert.Equal(
            FilterExpression.ContentSignature(x),
            FilterExpression.ContentSignature(FilterExpression.Simplify(withAny)));
    }

    [Fact]
    public void NeverIsAlwaysFalse()
    {
        Assert.True(FilterExpression.IsAlwaysFalse(FilterExpression.Never));
        Assert.True(FilterExpression.IsAlwaysTrue(FilterExpression.Any));
    }
}

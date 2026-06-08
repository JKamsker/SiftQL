using SiftQL.Expressions;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionFactoryTests
{
    [Fact]
    public void AndIgnoresAnyChildren()
    {
        var condition = FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(1L));

        FilterExpression combined = FilterExpression.And(FilterExpression.Any, condition);

        Assert.Same(condition, combined);
    }

    [Fact]
    public void OrWithAnyReturnsAny()
    {
        var condition = FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(1L));

        FilterExpression combined = FilterExpression.Or(condition, FilterExpression.Any);

        Assert.Equal(FilterExpressionKind.Any, combined.Kind);
    }

    [Fact]
    public void OrRejectsEmptyChildren()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            FilterExpression.Or());
    }

    [Fact]
    public void AndRejectsNullChildren()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            FilterExpression.And(FilterExpression.Any, null!));
    }

    [Fact]
    public void OrRejectsNullChildren()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            FilterExpression.Or(null!));
    }

    [Fact]
    public void InRejectsNullValues()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            FilterExpression.In("ItemId", [FilterValue.From(1L), null!]));
    }
}

using SiftQL.Expressions;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionFactoryTests
{
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

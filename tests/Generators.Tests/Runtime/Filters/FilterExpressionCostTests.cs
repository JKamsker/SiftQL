using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionCostTests
{
    [Fact]
    public void Cost_Any_IsZero()
    {
        int cost = FilterExpressionCost.Estimate(FilterExpression.Any);
        Assert.Equal(0, cost);
    }

    [Fact]
    public void Cost_Compare_Equal_IsOne()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Equal(1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Compare_NotEqual_IsTwo()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.NotEqual, FilterValue.From(1L));
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Compare_GreaterThan_IsTwo()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.GreaterThan, FilterValue.From(1L));
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Compare_StringContains_IsEight()
    {
        var expr = FilterExpression.StringContains("Name", FilterValue.From("ell"));
        Assert.Equal(8, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Exists_IsOne()
    {
        var expr = FilterExpression.Exists("ItemId");
        Assert.Equal(1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_In_ScalesWithValues()
    {
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From((long)i)).ToArray();
        var expr = FilterExpression.In("ItemId", values);
        Assert.Equal(4 + 5, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_In_CapsAtSixteen()
    {
        var values = Enumerable.Range(0, 20).Select(i => FilterValue.From((long)i)).ToArray();
        var expr = FilterExpression.In("ItemId", values);
        Assert.Equal(4 + 16, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Contains_IsThirtyTwo()
    {
        var expr = FilterExpression.Contains("Items", FilterValue.From(1L));
        Assert.Equal(32, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Not_IncludesChildCost()
    {
        var inner = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var expr = FilterExpression.Not(inner);
        Assert.Equal(8 + 1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_And_SumsChildren()
    {
        var child1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var child2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        var expr = FilterExpression.And(child1, child2);
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Or_AddsSixteenPlusChildren()
    {
        var child1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var child2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        var expr = FilterExpression.Or(child1, child2);
        Assert.Equal(16 + 2, FilterExpressionCost.Estimate(expr));
    }
}

using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionCost
{
    public static int Estimate(FilterExpression expression) =>
        expression.Kind switch
        {
            FilterExpressionKind.Any => 0,
            FilterExpressionKind.Compare => CompareCost(expression),
            FilterExpressionKind.Exists => 1,
            FilterExpressionKind.In => 4 + Math.Min(expression.Values.Length, 16),
            FilterExpressionKind.Contains => 32,
            FilterExpressionKind.Not => 8 + ChildrenCost(expression),
            FilterExpressionKind.And => ChildrenCost(expression),
            FilterExpressionKind.Or => 16 + ChildrenCost(expression),
            _ => 64,
        };

    private static int CompareCost(FilterExpression expression) =>
        expression.Operator == FilterOperator.Equal ? 1 : 2;

    private static int ChildrenCost(FilterExpression expression)
    {
        int cost = 0;
        for (int i = 0; i < expression.Children.Length; i++)
            cost += Estimate(expression.Children[i]);
        return cost;
    }
}

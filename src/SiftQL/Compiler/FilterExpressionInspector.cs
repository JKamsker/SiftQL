using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionInspector
{
    public static bool HasSelectiveNode(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.Compare or
            FilterExpressionKind.In or
            FilterExpressionKind.Contains ||
        HasSelectiveChild(expression.Children);

    public static int PromotionMinimumEvaluations(FilterExpression expression) =>
        expression.Kind switch
        {
            FilterExpressionKind.Contains => 50_000,
            FilterExpressionKind.In when expression.Values.Length >= 8 => 50_000,
            FilterExpressionKind.And or FilterExpressionKind.Or =>
                CompositePromotionMinimum(expression),
            _ => 100_000,
        };

    private static int CompositePromotionMinimum(FilterExpression expression)
    {
        int minimum = 100_000;
        for (int i = 0; i < expression.Children.Length; i++)
            minimum = Math.Min(minimum, PromotionMinimumEvaluations(expression.Children[i]));

        return expression.Children.Length >= 3 ? Math.Min(minimum, 75_000) : minimum;
    }

    private static bool HasSelectiveChild(FilterExpression[] children)
    {
        for (int i = 0; i < children.Length; i++)
        {
            if (HasSelectiveNode(children[i]))
                return true;
        }

        return false;
    }
}

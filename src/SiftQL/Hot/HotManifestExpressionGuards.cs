using SiftQL.Expressions;

namespace SiftQL.Hot;

internal static class HotManifestExpressionGuards
{
    public static bool ContainsUnsupportedFilterNode(FilterExpression expression)
    {
        if (expression is null)
            return true;
        if (expression.Children is null || expression.Values is null)
            return true;

        if (!IsSupportedFilterNode(expression.Kind))
            return true;
        if (!IsValidFilterShape(expression))
            return true;

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (ContainsUnsupportedFilterNode(expression.Children[i]))
                return true;
        }

        return false;
    }

    public static bool ContainsNonFiniteNumber(FilterExpression expression)
    {
        if (expression is null || expression.Children is null || expression.Values is null)
            return false;

        if (IsNonFiniteNumber(expression.Value))
            return true;

        for (int i = 0; i < expression.Values.Length; i++)
        {
            if (IsNonFiniteNumber(expression.Values[i]))
                return true;
        }

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (ContainsNonFiniteNumber(expression.Children[i]))
                return true;
        }

        return false;
    }

    public static bool ContainsNonFiniteNumber(EventProjectionExpression projection)
    {
        if (projection.Includes is null)
            return false;

        for (int i = 0; i < projection.Includes.Length; i++)
        {
            EventProjectionInclude? include = projection.Includes[i];
            if (include?.Arguments is null)
                continue;

            for (int j = 0; j < include.Arguments.Length; j++)
            {
                EventProjectionArgument? argument = include.Arguments[j];
                if (argument is not null &&
                    argument.Kind == EventProjectionArgumentKind.Value &&
                    IsNonFiniteNumber(argument.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNonFiniteNumber(FilterValue? value) =>
        value?.Kind == FilterValueKind.Number &&
        (double.IsNaN(value.Number) || double.IsInfinity(value.Number));

    private static bool IsSupportedFilterNode(FilterExpressionKind kind) =>
        kind is FilterExpressionKind.Any or
            FilterExpressionKind.And or
            FilterExpressionKind.Or or
            FilterExpressionKind.Not or
            FilterExpressionKind.Compare or
            FilterExpressionKind.In or
            FilterExpressionKind.Exists or
            FilterExpressionKind.Contains;

    private static bool IsValidFilterShape(FilterExpression expression) =>
        expression.Kind switch
        {
            FilterExpressionKind.Any => expression.Children.Length == 0,
            FilterExpressionKind.And or FilterExpressionKind.Or => expression.Children.Length > 0,
            FilterExpressionKind.Not => expression.Children.Length == 1,
            FilterExpressionKind.Compare or FilterExpressionKind.Contains =>
                expression.Children.Length == 0 &&
                expression.Value is not null &&
                !string.IsNullOrWhiteSpace(expression.Field),
            FilterExpressionKind.In =>
                expression.Children.Length == 0 &&
                expression.Values.Length > 0 &&
                !string.IsNullOrWhiteSpace(expression.Field),
            FilterExpressionKind.Exists =>
                expression.Children.Length == 0 &&
                !string.IsNullOrWhiteSpace(expression.Field),
            _ => false,
        };
}

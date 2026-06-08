using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionSnapshot
{
    public static FilterExpression Clone(FilterExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression with
        {
            Value = CloneValue(expression.Value),
            Values = expression.Values.Select(static value => CloneValue(value)!).ToArray(),
            Children = expression.Children.Select(Clone).ToArray(),
        };
    }

    public static FilterValue? CloneValue(FilterValue? value) =>
        value is null ? null : value with { };
}

internal static class ProjectionExpressionSnapshot
{
    public static EventProjectionExpression Clone(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection with
        {
            Fields = projection.Fields?.Select(CloneField).ToArray()!,
            Includes = projection.Includes?.Select(CloneInclude).ToArray()!,
        };
    }

    private static EventProjectionField CloneField(EventProjectionField field) =>
        field is null ? null! : field with { };

    private static EventProjectionInclude CloneInclude(EventProjectionInclude include) =>
        include is null
            ? null!
            : include with
            {
                Arguments = include.Arguments?.Select(CloneArgument).ToArray()!,
            };

    private static EventProjectionArgument CloneArgument(EventProjectionArgument argument) =>
        argument is null
            ? null!
            : argument with
            {
                Value = FilterExpressionSnapshot.CloneValue(argument.Value)!,
                SourcePath = argument.SourcePath,
            };
}

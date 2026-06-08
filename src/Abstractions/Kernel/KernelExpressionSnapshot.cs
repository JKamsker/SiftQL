using SiftQL.Expressions;

namespace SiftQL;

internal static class KernelExpressionSnapshot
{
    public static EventPipelineExpression Clone(EventPipelineExpression pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return pipeline with
        {
            Stages = pipeline.Stages?.Select(CloneStage).ToArray()!,
        };
    }

    public static FilterExpression Clone(FilterExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression with
        {
            Value = CloneValue(expression.Value),
            Values = expression.Values?.Select(static value => CloneValue(value)!).ToArray()!,
            Children = expression.Children?.Select(CloneFilter).ToArray()!,
        };
    }

    public static EventProjectionExpression Clone(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection with
        {
            Fields = projection.Fields?.Select(CloneField).ToArray()!,
            Includes = projection.Includes?.Select(CloneInclude).ToArray()!,
        };
    }

    private static EventPipelineStage CloneStage(EventPipelineStage stage) =>
        stage is null
            ? null!
            : stage with
            {
                Filter = Clone(stage.Filter),
                Projection = Clone(stage.Projection),
            };

    private static FilterExpression CloneFilter(FilterExpression expression) =>
        expression is null ? null! : Clone(expression);

    private static FilterValue? CloneValue(FilterValue? value) =>
        value is null ? null : value with { };

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
                Value = CloneValue(argument.Value)!,
            };
}

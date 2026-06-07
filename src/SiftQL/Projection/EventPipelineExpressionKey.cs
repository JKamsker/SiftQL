using System.Text;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;

namespace SiftQL.Projection;

internal sealed class EventPipelineExpressionKey : IEquatable<EventPipelineExpressionKey>
{
    private readonly int _hashCode;

    private EventPipelineExpressionKey(StructuralKeyArray<EventPipelineStageKey> stages)
    {
        Stages = stages;
        _hashCode = Stages.GetHashCode();
    }

    public StructuralKeyArray<EventPipelineStageKey> Stages { get; }

    public static EventPipelineExpressionKey From(EventPipelineExpression pipeline) =>
        From(pipeline, includeParameterValues: false);

    public static EventPipelineExpressionKey FromWithParameterValues(EventPipelineExpression pipeline) =>
        From(pipeline, includeParameterValues: true);

    private static EventPipelineExpressionKey From(
        EventPipelineExpression pipeline,
        bool includeParameterValues) =>
        new(pipeline.Stages.Length == 0
            ? StructuralKeyArray<EventPipelineStageKey>.Empty
            : StructuralKeyArray<EventPipelineStageKey>.From(
                pipeline.Stages,
                includeParameterValues,
                static (stage, includeValues) => EventPipelineStageKey.From(stage, includeValues)));

    public bool Equals(EventPipelineExpressionKey? other) =>
        ReferenceEquals(this, other) || (other is not null && Stages.Equals(other.Stages));

    public override bool Equals(object? obj) =>
        obj is EventPipelineExpressionKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("pipeline[").Append(Stages.Count).Append(']');
        for (int i = 0; i < Stages.Count; i++)
            Stages[i].AppendTo(builder);
        return builder.ToString();
    }
}

internal readonly record struct EventPipelineStageKey(
    EventPipelineStageKind Kind,
    FilterExpressionKey? Filter,
    ProjectionExpressionKey? Projection)
{
    public static EventPipelineStageKey From(EventPipelineStage stage, bool includeParameterValues) =>
        stage.Kind == EventPipelineStageKind.Filter
            ? new(stage.Kind, FilterExpressionFingerprint.CreateKey(FilterForKey(stage.Filter, includeParameterValues)), null)
            : new(stage.Kind, null, ProjectionExpressionFingerprint.CreateKey(ProjectionForKey(stage.Projection, includeParameterValues)));

    public void AppendTo(StringBuilder builder)
    {
        builder.Append('{').Append((int)Kind).Append(':');
        builder.Append(Filter?.ToString() ?? Projection?.ToString() ?? string.Empty);
        builder.Append('}');
    }

    private static FilterExpression FilterForKey(
        FilterExpression expression,
        bool includeParameterValues) =>
        includeParameterValues ? RemoveParameterKeys(expression) : expression;

    private static EventProjectionExpression ProjectionForKey(
        EventProjectionExpression projection,
        bool includeParameterValues) =>
        includeParameterValues ? RemoveParameterKeys(projection) : projection;

    private static FilterExpression RemoveParameterKeys(FilterExpression expression) =>
        expression with
        {
            Value = RemoveParameterKey(expression.Value),
            Values = expression.Values.Select(static value => RemoveParameterKey(value)!).ToArray(),
            Children = expression.Children.Select(RemoveParameterKeys).ToArray(),
        };

    private static EventProjectionExpression RemoveParameterKeys(EventProjectionExpression projection) =>
        projection with
        {
            Includes = projection.Includes
                .Select(static include => include with
                {
                    Arguments = include.Arguments
                        .Select(static argument => argument with
                        {
                            Value = RemoveParameterKey(argument.Value)!,
                        })
                        .ToArray(),
                })
                .ToArray(),
        };

    private static FilterValue? RemoveParameterKey(FilterValue? value) =>
        value is null || string.IsNullOrWhiteSpace(value.ParameterKey)
            ? value
            : value with { ParameterKey = null };
}

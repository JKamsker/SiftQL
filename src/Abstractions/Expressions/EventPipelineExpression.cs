using SiftQL.Translation;

namespace SiftQL.Expressions;

public enum EventPipelineStageKind
{
    Filter = 0,
    Projection = 1,
}

public sealed record EventPipelineStage
{
    public EventPipelineStageKind Kind { get; init; }
    public FilterExpression Filter { get; init; } = FilterExpression.Any;
    public EventProjectionExpression Projection { get; init; } = EventProjectionExpression.Default;
}

public sealed record EventPipelineExpression
{
    public static EventPipelineExpression Default { get; } = new();

    public EventPipelineStage[] Stages { get; init; } = [];

    public bool IsDefault => Stages.Length == 0;
    public bool HasProjection => Stages.Any(static stage => stage.Kind == EventPipelineStageKind.Projection);

    public EventPipelineExpression AppendFilter(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Kind == FilterExpressionKind.Any)
            return this;

        return this with
        {
            Stages =
            [
                .. Stages,
                new EventPipelineStage { Kind = EventPipelineStageKind.Filter, Filter = filter },
            ],
        };
    }

    public EventPipelineExpression AppendSourceFilter(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Kind == FilterExpressionKind.Any)
            return this;

        int projectionIndex = Array.FindIndex(
            Stages,
            static stage => stage.Kind == EventPipelineStageKind.Projection);
        if (projectionIndex < 0)
            return AppendFilter(filter);

        var stages = new EventPipelineStage[Stages.Length + 1];
        Array.Copy(Stages, 0, stages, 0, projectionIndex);
        stages[projectionIndex] = new EventPipelineStage
        {
            Kind = EventPipelineStageKind.Filter,
            Filter = filter,
        };
        Array.Copy(Stages, projectionIndex, stages, projectionIndex + 1, Stages.Length - projectionIndex);
        return this with { Stages = stages };
    }

    public EventPipelineExpression AppendProjection(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return this with
        {
            Stages =
            [
                .. Stages,
                new EventPipelineStage { Kind = EventPipelineStageKind.Projection, Projection = projection },
            ],
        };
    }

    public EventPipelineExpression AppendOrMergeLastProjection(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (Stages.Length == 0 ||
            Stages[^1].Kind != EventPipelineStageKind.Projection)
        {
            return AppendProjection(projection);
        }

        var stages = Stages.ToArray();
        EventProjectionExpression previous = stages[^1].Projection;
        stages[^1] = new EventPipelineStage
        {
            Kind = EventPipelineStageKind.Projection,
            Projection = previous with
            {
                Fields = [.. previous.Fields, .. projection.Fields],
                Includes = [.. previous.Includes, .. projection.Includes],
            },
        };
        return this with { Stages = stages };
    }

    public static EventPipelineExpression From(
        FilterExpression? filter,
        EventProjectionExpression? projection)
    {
        EventPipelineExpression pipeline = Default;
        if (filter is not null && filter.Kind != FilterExpressionKind.Any)
            pipeline = pipeline.AppendFilter(filter);
        if (projection is not null && !projection.IsDefault)
            pipeline = pipeline.AppendProjection(projection);
        return pipeline;
    }
}

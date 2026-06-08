using SiftQL.Expressions;

namespace SiftQL;

internal static class QueryKernelPipelineState
{
    public static FilterExpression SourceFilter(EventPipelineExpression pipeline)
    {
        var filters = new List<FilterExpression>();
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Projection)
                break;
            if (stage.Kind == EventPipelineStageKind.Filter &&
                stage.Filter.Kind != FilterExpressionKind.Any)
            {
                filters.Add(stage.Filter);
            }
        }

        return FilterExpression.And(filters.ToArray());
    }

    public static EventProjectionExpression LastProjectionOrDefault(EventPipelineExpression pipeline)
    {
        for (int i = pipeline.Stages.Length - 1; i >= 0; i--)
        {
            if (pipeline.Stages[i].Kind == EventPipelineStageKind.Projection)
                return pipeline.Stages[i].Projection;
        }

        return EventProjectionExpression.Default;
    }

    public static EventProjectionExpression ProjectionInputForNextProjection(EventPipelineExpression pipeline)
    {
        int index = LastProjectionIndex(pipeline);
        if (index < 0)
            return EventProjectionExpression.Default;
        if (index != pipeline.Stages.Length - 1)
            return pipeline.Stages[index].Projection;

        for (int i = index - 1; i >= 0; i--)
        {
            if (pipeline.Stages[i].Kind == EventPipelineStageKind.Projection)
                return pipeline.Stages[i].Projection;
        }

        return EventProjectionExpression.Default;
    }

    private static int LastProjectionIndex(EventPipelineExpression pipeline)
    {
        for (int i = pipeline.Stages.Length - 1; i >= 0; i--)
        {
            if (pipeline.Stages[i].Kind == EventPipelineStageKind.Projection)
                return i;
        }

        return -1;
    }
}

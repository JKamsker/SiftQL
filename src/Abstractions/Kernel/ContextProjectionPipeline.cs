using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL;

internal static class ContextProjectionPipeline
{
    public static EventPipelineExpression AddIncludes(
        EventPipelineExpression pipeline,
        IReadOnlyList<EventProjectionInclude> includes,
        IReadOnlyList<EventProjectionField>? sourceFields = null)
    {
        if (includes.Count == 0 && (sourceFields is null || sourceFields.Count == 0))
            return pipeline;

        int projectionIndex = Array.FindIndex(
            pipeline.Stages,
            static stage => stage.Kind == EventPipelineStageKind.Projection);
        if (projectionIndex < 0)
        {
            EventProjectionExpression projection = EventProjectionExpression.Default with
            {
                Fields = sourceFields?.ToArray() ?? [],
                Includes = includes.ToArray(),
            };
            return pipeline.AppendProjection(projection);
        }

        var stages = pipeline.Stages.ToArray();
        EventProjectionExpression previous = stages[projectionIndex].Projection;
        stages[projectionIndex] = new EventPipelineStage
        {
            Kind = EventPipelineStageKind.Projection,
            Projection = previous with
            {
                Fields = sourceFields is { Count: > 0 }
                    ? [.. previous.Fields, .. sourceFields]
                    : previous.Fields,
                Includes = includes.Count == 0
                    ? previous.Includes
                    : [.. previous.Includes, .. includes],
            },
        };

        return pipeline with { Stages = stages };
    }

    public static bool HasProjection(EventPipelineExpression pipeline) =>
        pipeline.Stages.Any(static stage => stage.Kind == EventPipelineStageKind.Projection);

    public static string ProjectedFieldName(EventPipelineExpression pipeline, string sourcePath)
    {
        EventProjectionExpression previous = QueryKernelPipelineState.LastProjectionOrDefault(pipeline);
        for (int i = previous.Fields.Length - 1; i >= 0; i--)
        {
            EventProjectionField field = previous.Fields[i];
            if (string.Equals(field.Path, sourcePath, StringComparison.OrdinalIgnoreCase))
                return field.Name;
        }

        return sourcePath;
    }

    public static string ProjectedPath(EventPipelineExpression pipeline, string sourcePath) =>
        ProjectedEventPaths.Field(ProjectedFieldName(pipeline, sourcePath));
}

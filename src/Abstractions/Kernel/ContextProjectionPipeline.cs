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

    public static EventPipelineExpression AppendSelectorWithIncludes(
        EventPipelineExpression pipeline,
        EventProjectionExpression projection,
        bool projected)
    {
        EventPipelineExpression withIncludes = AddIncludes(
            pipeline,
            projection.Includes,
            projected ? [] : projection.Fields);
        EventProjectionField[] finalFields =
        [
            .. projection.Fields.Select(field => FinalField(field, projected)),
            .. projection.Includes.Select(static include =>
                new EventProjectionField(ProjectedEventPaths.Context(include.ResultName), include.ResultName)),
        ];

        return withIncludes.AppendProjection(EventProjectionExpression.Default.WithFields(finalFields));
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

            if (sourcePath.Length > field.Path.Length &&
                sourcePath[field.Path.Length] == '.' &&
                sourcePath.StartsWith(field.Path, StringComparison.OrdinalIgnoreCase))
            {
                return field.Name + sourcePath[field.Path.Length..];
            }
        }

        return sourcePath;
    }

    public static string ProjectedPath(EventPipelineExpression pipeline, string sourcePath) =>
        ProjectedEventPaths.Field(ProjectedFieldName(pipeline, sourcePath));

    private static EventProjectionField FinalField(EventProjectionField field, bool projected) =>
        projected
            ? field
            : new EventProjectionField(ProjectedEventPaths.Field(field.Name), field.Name);
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Projection;

internal static class EventPipelineNormalizer
{
    public static EventPipelineExpression Normalize(
        Type subjectType,
        EventPipelineExpression? pipeline,
        Func<string, Exception>? errorFactory = null)
    {
        pipeline ??= EventPipelineExpression.Default;
        ValidateStages(pipeline, errorFactory);
        return HasProjection(pipeline)
            ? pipeline
            : pipeline.AppendProjection(DefaultProjection(subjectType, pipeline));
    }

    private static EventProjectionExpression DefaultProjection(
        Type subjectType,
        EventPipelineExpression pipeline) =>
        subjectType == typeof(ProjectedEvent)
            ? ProjectedFilterFieldProjection(pipeline)
            : EventProjectionExpression.Default;

    private static EventProjectionExpression ProjectedFilterFieldProjection(
        EventPipelineExpression pipeline)
    {
        var fields = new List<EventProjectionField>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Filter)
                CollectProjectedFields(stage.Filter, fields, names);
        }

        return fields.Count == 0
            ? EventProjectionExpression.Default
            : EventProjectionExpression.Default.WithFields(fields.ToArray());
    }

    private static void CollectProjectedFields(
        FilterExpression expression,
        List<EventProjectionField> fields,
        HashSet<string> names)
    {
        if (ProjectedEventPaths.TrySplit(expression.Field, out _, out string name) &&
            names.Add(name))
        {
            fields.Add(new EventProjectionField(expression.Field, name));
        }

        for (int i = 0; i < expression.Children.Length; i++)
            CollectProjectedFields(expression.Children[i], fields, names);
    }

    private static void ValidateStages(
        EventPipelineExpression pipeline,
        Func<string, Exception>? errorFactory)
    {
        if (pipeline.Stages is null)
            throw Error(errorFactory, "Pipeline stages cannot be null.");

        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage? stage = pipeline.Stages[i];
            if (stage is null)
                throw Error(errorFactory, "Pipeline stages cannot contain null.");
            if (stage.Kind is not EventPipelineStageKind.Filter and
                not EventPipelineStageKind.Projection)
            {
                throw Error(errorFactory, $"Pipeline stage kind '{stage.Kind}' is not supported.");
            }

            if (stage.Kind == EventPipelineStageKind.Filter)
            {
                if (stage.Filter is null)
                    throw Error(errorFactory, "Pipeline filter stages require a filter.");
                FilterExpressionShapeValidator.Validate(stage.Filter, errorFactory);
            }

            if (stage.Kind == EventPipelineStageKind.Projection &&
                stage.Projection is null)
            {
                throw Error(errorFactory, "Pipeline projection stages require a projection.");
            }
        }
    }

    private static bool HasProjection(EventPipelineExpression pipeline)
    {
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            if (pipeline.Stages[i].Kind == EventPipelineStageKind.Projection)
                return true;
        }

        return false;
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

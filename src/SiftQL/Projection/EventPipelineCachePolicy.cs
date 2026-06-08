using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;

namespace SiftQL.Projection;

internal static class EventPipelineCachePolicy
{
    public static EventPipelineExpression Snapshot(EventPipelineExpression pipeline) =>
        pipeline with
        {
            Stages = pipeline.Stages
                .Select(static stage => stage.Kind == EventPipelineStageKind.Filter
                    ? stage with { Filter = FilterExpressionSnapshot.Clone(stage.Filter) }
                    : stage with { Projection = ProjectionExpressionSnapshot.Clone(stage.Projection) })
                .ToArray(),
        };

    public static bool ShouldBypassCache(
        EventPipelineExpression pipeline,
        EventPipelineCompilerOptions options) =>
        HasInvalidProjectionShape(pipeline) ||
        HasParameters(pipeline) ||
        HasTieredOptions(options) ||
        PrecompiledTieredProviderRegistry.IsolatedScopeActive;

    private static bool HasParameters(EventPipelineExpression pipeline)
    {
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Filter &&
                FilterExpressionParameters.HasParameters(stage.Filter))
            {
                return true;
            }

            if (stage.Kind == EventPipelineStageKind.Projection &&
                ProjectionExpressionParameters.HasParameters(stage.Projection))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTieredOptions(EventPipelineCompilerOptions options) =>
        options.FilterOptions.Mode == FilterCompilationMode.Tiered ||
        options.ProjectionOptions.Mode == ProjectionCompilationMode.Tiered;

    private static bool HasInvalidProjectionShape(EventPipelineExpression pipeline)
    {
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind != EventPipelineStageKind.Projection)
                continue;

            if (stage.Projection.Fields is null || stage.Projection.Includes is null)
                return true;
            if (stage.Projection.Fields.Any(static field => field is null))
                return true;
            if (stage.Projection.Includes.Any(static include =>
                    include is null ||
                    include.Arguments is null ||
                    include.Arguments.Any(static argument => argument is null)))
            {
                return true;
            }
        }

        return false;
    }
}

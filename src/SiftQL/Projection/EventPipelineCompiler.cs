using System.Collections.Concurrent;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Projection;

public static class EventPipelineCompiler
{
    private const int MaxCachedPipelines = 2048;
    private static readonly ConcurrentDictionary<EventPipelineCacheKey, object> s_cache = new();
    private static int s_cacheCount;

    static EventPipelineCompiler()
    {
        PrecompiledTieredProviderRegistry.Changed += ClearCache;
    }

    public static CompiledEventPipeline<TContext> Compile<TContext>(
        Type subjectType,
        EventPipelineExpression? pipeline,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(compileInclude);
        ArgumentNullException.ThrowIfNull(options);
        EventPipelineExpression normalized = Snapshot(
            EventPipelineNormalizer.Normalize(subjectType, pipeline, errorFactory));
        IncludeCompilerKey includeCompilerKey = IncludeCompilerKey.From(compileInclude);
        if (HasInvalidProjectionShape(normalized) ||
            HasParameters(normalized) ||
            PrecompiledTieredProviderRegistry.IsolatedScopeActive)
        {
            return CompileUncached(subjectType, normalized, compileInclude, includeCompilerKey, options, errorFactory);
        }

        var key = new EventPipelineCacheKey(
            typeof(TContext),
            subjectType,
            EventPipelineExpressionKey.From(normalized),
            includeCompilerKey,
            PrecompiledTieredProviderRegistry.GlobalVersion,
            FilterSchema.Version,
            FilterCompilerOptionsCacheKey.From(options.FilterOptions),
            ProjectionCompilerOptionsCacheKey.From(options.ProjectionOptions));
        if (s_cache.TryGetValue(key, out object? cached))
            return (CompiledEventPipeline<TContext>)cached;

        EnsureCacheCapacity();
        var compiled = CompileUncached(subjectType, normalized, compileInclude, includeCompilerKey, options, errorFactory);
        if (s_cache.TryAdd(key, compiled))
        {
            Interlocked.Increment(ref s_cacheCount);
            return compiled;
        }

        return s_cache.TryGetValue(key, out object? raced)
            ? (CompiledEventPipeline<TContext>)raced
            : compiled;
    }

    private static void EnsureCacheCapacity()
    {
        if (Volatile.Read(ref s_cacheCount) < MaxCachedPipelines)
            return;

        ClearCache();
    }

    public static FilterExpression SourceFilter(EventPipelineExpression? pipeline)
    {
        EventPipelineExpression normalized = EventPipelineNormalizer.Normalize(typeof(object), pipeline);
        var filters = new List<FilterExpression>();
        for (int i = 0; i < normalized.Stages.Length; i++)
        {
            EventPipelineStage stage = normalized.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Projection)
                break;
            if (stage.Filter.Kind != FilterExpressionKind.Any)
                filters.Add(stage.Filter);
        }

        return FilterExpression.And(filters.ToArray());
    }

    public static EventPipelineExpression ProjectionDispatchPipeline(EventPipelineExpression? pipeline)
    {
        EventPipelineExpression normalized = EventPipelineNormalizer.Normalize(typeof(object), pipeline);
        int projectionIndex = Array.FindIndex(
            normalized.Stages,
            static stage => stage.Kind == EventPipelineStageKind.Projection);
        if (projectionIndex <= 0)
            return normalized;

        var stages = new EventPipelineStage[normalized.Stages.Length - projectionIndex];
        Array.Copy(normalized.Stages, projectionIndex, stages, 0, stages.Length);
        return normalized with { Stages = stages };
    }

    private static CompiledEventPipeline<TContext> CompileUncached<TContext>(
        Type subjectType,
        EventPipelineExpression pipeline,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        IncludeCompilerKey includeCompilerKey,
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory)
    {
        var stages = new List<PipelineStage<TContext>>();
        bool projected = subjectType == typeof(ProjectedEvent);
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Filter)
                stages.Add(CompileFilterStage<TContext>(subjectType, projected, stage.Filter, options, errorFactory));
            else
                stages.Add(CompileProjectionStage(subjectType, projected, stage.Projection, compileInclude, options, errorFactory));
            projected |= stage.Kind == EventPipelineStageKind.Projection;
        }

        return new CompiledEventPipeline<TContext>(
            "subject:" + SubjectKey(subjectType) + "|" +
            EventPipelineExpressionKey.FromWithParameterValues(pipeline) + "|include:" + includeCompilerKey,
            SourceFilter(pipeline),
            stages);
    }

    private static string SubjectKey(Type subjectType) =>
        subjectType.AssemblyQualifiedName ?? subjectType.FullName ?? subjectType.Name;

    private static PipelineStage<TContext> CompileFilterStage<TContext>(
        Type sourceType,
        bool projected,
        FilterExpression filter,
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory)
    {
        if (!projected)
            return new PipelineFilterStage<TContext>(
                FilterCompiler.Compile(sourceType, filter, options.FilterOptions, errorFactory));

        FilterSchema schema = ProjectedEventFilterSchema.ForFilter(filter);
        return new PipelineFilterStage<TContext>(
            FilterCompiler.CompileWithSchema(
                typeof(ProjectedEvent),
                filter,
                options.FilterOptions,
                errorFactory,
                _ => schema));
    }

    private static PipelineStage<TContext> CompileProjectionStage<TContext>(
        Type sourceType,
        bool projected,
        EventProjectionExpression projection,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory)
    {
        if (!projected)
        {
            return new PipelineProjectionStage<TContext>(
                ProjectionCompiler.Compile(sourceType, projection, compileInclude, options.ProjectionOptions, errorFactory));
        }

        FilterSchema schema = ProjectedEventFilterSchema.ForProjection(projection);
        return new PipelineProjectionStage<TContext>(
            ProjectionCompiler.CompileWithSchema(
                typeof(ProjectedEvent),
                projection,
                RejectProjectedInclude<TContext>,
                options.ProjectionOptions,
                errorFactory,
                _ => schema,
                sourceType));
    }

    private static CompiledProjection<TContext>.IncludeProjector RejectProjectedInclude<TContext>(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new FilterValidationException(
            $"Projection include '{include.Intrinsic}' cannot run after a projected stage.");
    }

    private static EventPipelineExpression Snapshot(EventPipelineExpression pipeline) =>
        pipeline with
        {
            Stages = pipeline.Stages
                .Select(static stage => stage.Kind == EventPipelineStageKind.Filter
                    ? stage with { Filter = FilterExpressionSnapshot.Clone(stage.Filter) }
                    : stage with { Projection = ProjectionExpressionSnapshot.Clone(stage.Projection) })
                .ToArray(),
        };

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

    private static void ClearCache()
    {
        s_cache.Clear();
        Volatile.Write(ref s_cacheCount, 0);
    }

}

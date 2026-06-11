using System.Collections.Concurrent;
using System.Globalization;
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
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory = null) =>
        Compile(
            subjectType,
            pipeline,
            ProjectionContextIncludeCompiler.Compile<TContext>,
            options,
            errorFactory);

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
        EventPipelineExpression normalized = EventPipelineCachePolicy.Snapshot(
            EventPipelineNormalizer.Normalize(subjectType, pipeline, errorFactory));
        IncludeCompilerKey includeCompilerKey = IncludeCompilerKey.From(compileInclude);
        int schemaVersion = FilterSchema.Version;
        if (EventPipelineCachePolicy.ShouldBypassCache(normalized, options))
        {
            return CompileUncached(
                subjectType,
                normalized,
                compileInclude,
                includeCompilerKey,
                schemaVersion,
                options,
                errorFactory);
        }

        var key = new EventPipelineCacheKey(
            typeof(TContext),
            subjectType,
            EventPipelineExpressionKey.From(normalized),
            includeCompilerKey,
            PrecompiledTieredProviderRegistry.GlobalVersion,
            schemaVersion,
            FilterCompilerOptionsCacheKey.From(options.FilterOptions),
            ProjectionCompilerOptionsCacheKey.From(options.ProjectionOptions));
        if (s_cache.TryGetValue(key, out object? cached))
            return (CompiledEventPipeline<TContext>)cached;

        EnsureCacheCapacity();
        var compiled = CompileUncached(
            subjectType,
            normalized,
            compileInclude,
            includeCompilerKey,
            schemaVersion,
            options,
            errorFactory);
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

        return FilterExpressionSnapshot.Clone(FilterExpression.And(filters.ToArray()));
    }

    public static EventPipelineExpression ProjectionDispatchPipeline(EventPipelineExpression? pipeline)
    {
        bool referencesProjectedFields = ReferencesProjectedFields(pipeline);
        Type subjectType = referencesProjectedFields
            ? typeof(ProjectedEvent)
            : typeof(object);
        EventPipelineExpression normalized = EventPipelineNormalizer.Normalize(subjectType, pipeline);
        if (referencesProjectedFields && pipeline?.HasProjection != true)
            return normalized;

        int projectionIndex = Array.FindIndex(
            normalized.Stages,
            static stage => stage.Kind == EventPipelineStageKind.Projection);
        if (projectionIndex <= 0)
            return normalized;

        var stages = new List<EventPipelineStage>(normalized.Stages.Length - projectionIndex);
        for (int i = 0; i < normalized.Stages.Length; i++)
        {
            EventPipelineStage stage = normalized.Stages[i];
            if (i < projectionIndex && stage.Kind == EventPipelineStageKind.Filter)
            {
                FilterExpression? dispatchFilter = ProjectionDispatchFilter(stage.Filter);
                if (dispatchFilter is null)
                    continue;

                stage = stage with { Filter = dispatchFilter };
            }

            stages.Add(stage);
        }

        return normalized with { Stages = stages.ToArray() };
    }

    private static FilterExpression? ProjectionDispatchFilter(FilterExpression filter)
    {
        if (!ReferencesProjectedFields(filter))
            return null;
        if (filter.Kind != FilterExpressionKind.And)
            return filter;

        FilterExpression[] projected = filter.Children
            .Where(ReferencesProjectedFields)
            .ToArray();
        return projected.Length switch
        {
            0 => null,
            1 => projected[0],
            _ => filter with { Children = projected },
        };
    }

    private static bool ReferencesProjectedFields(EventPipelineExpression? pipeline)
    {
        if (pipeline?.Stages is null)
            return false;

        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage? stage = pipeline.Stages[i];
            if (stage?.Kind == EventPipelineStageKind.Filter &&
                ReferencesProjectedFields(stage.Filter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesProjectedFields(FilterExpression? expression)
    {
        if (expression is null)
            return false;
        if (ProjectedEventPaths.TrySplit(expression.Field, out _, out _))
            return true;
        if (expression.Children is null)
            return false;

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (ReferencesProjectedFields(expression.Children[i]))
                return true;
        }

        return false;
    }

    private static CompiledEventPipeline<TContext> CompileUncached<TContext>(
        Type subjectType,
        EventPipelineExpression pipeline,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        IncludeCompilerKey includeCompilerKey,
        int schemaVersion,
        EventPipelineCompilerOptions options,
        Func<string, Exception>? errorFactory)
    {
        var stages = new List<PipelineStage<TContext>>();
        bool projectedSource = subjectType == typeof(ProjectedEvent);
        bool priorProjection = false;
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            bool projected = projectedSource || priorProjection;
            if (stage.Kind == EventPipelineStageKind.Filter)
                stages.Add(CompileFilterStage<TContext>(subjectType, projected, stage.Filter, options, errorFactory));
            else
                stages.Add(CompileProjectionStage(
                    subjectType,
                    projected,
                    priorProjection,
                    stage.Projection,
                    compileInclude,
                    options,
                    errorFactory));
            priorProjection |= stage.Kind == EventPipelineStageKind.Projection;
        }

        return new CompiledEventPipeline<TContext>(
            "subject:" + SubjectKey(subjectType) + "|" +
            "schema:" + schemaVersion.ToString(CultureInfo.InvariantCulture) + "|" +
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
        bool priorProjection,
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

        ValidateProjectionShape(projection, errorFactory);
        FilterSchema schema = ProjectedEventFilterSchema.ForProjection(projection);
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> includeCompiler =
            priorProjection ? RejectProjectedInclude<TContext> : compileInclude;
        return new PipelineProjectionStage<TContext>(
            ProjectionCompiler.CompileWithSchema(
                typeof(ProjectedEvent),
                projection,
                includeCompiler,
                options.ProjectionOptions,
                errorFactory,
                _ => schema,
                sourceType));
    }

    private static void ValidateProjectionShape(
        EventProjectionExpression projection,
        Func<string, Exception>? errorFactory)
    {
        if (projection.Fields is null)
            throw Error(errorFactory, "Projection fields cannot be null.");
        if (projection.Includes is null)
            throw Error(errorFactory, "Projection includes cannot be null.");
    }

    private static CompiledProjection<TContext>.IncludeProjector RejectProjectedInclude<TContext>(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new FilterValidationException(
            $"Projection include '{include.Intrinsic}' cannot run after a projected stage.");
    }

    private static void ClearCache()
    {
        s_cache.Clear();
        Volatile.Write(ref s_cacheCount, 0);
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);

}

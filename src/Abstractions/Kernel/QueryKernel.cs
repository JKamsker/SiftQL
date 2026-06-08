using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

using SiftQL.Translation;

namespace SiftQL;

public static class QueryKernel
{
    public static QueryKernel<TSubject> Any<TSubject>() =>
        new(FilterExpression.Any);

    public static QueryKernel<TSubject> For<TSubject>() =>
        Any<TSubject>();
}

public sealed record QueryKernel<TSubject>
{
    private EventPipelineExpression _pipeline = EventPipelineExpression.Default;

    public QueryKernel()
    {
    }

    public QueryKernel(FilterExpression filter)
        : this(filter, EventProjectionExpression.Default)
    {
    }

    public QueryKernel(FilterExpression filter, EventProjectionExpression projection)
        : this(filter, projection, EventPipelineExpression.From(filter, projection))
    {
    }

    private QueryKernel(
        FilterExpression filter,
        EventProjectionExpression projection,
        EventPipelineExpression pipeline)
    {
        Filter = filter ?? throw new ArgumentNullException(nameof(filter));
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public FilterExpression Filter { get; init; } = FilterExpression.Any;
    public EventProjectionExpression Projection { get; init; } = EventProjectionExpression.Default;
    public EventPipelineExpression Pipeline
    {
        get => _pipeline.IsDefault
            ? EventPipelineExpression.From(Filter, Projection)
            : _pipeline;
        init => _pipeline = value ?? throw new ArgumentNullException(nameof(value));
    }

    public QueryKernel<TSubject> WithSourceFilter(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        FilterExpression translated = KernelParameterKeyRewriter.Rebase(
            filter,
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        return new QueryKernel<TSubject>(
            FilterExpression.And(Filter, translated),
            Projection,
            Pipeline.AppendSourceFilter(translated));
    }

    public QueryKernel<TSubject> Where(Expression<Func<TSubject, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var translated = KernelParameterKeyRewriter.Rebase(
            KernelExpressionTranslator.Translate(predicate),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        return new QueryKernel<TSubject>(
            FilterExpression.And(Filter, translated),
            Projection,
            Pipeline.AppendSourceFilter(translated));
    }

    public QueryKernel<TSubject> WhereProjected(Expression<Func<ProjectedEvent, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var translated = KernelParameterKeyRewriter.Rebase(
            KernelExpressionTranslator.Translate(predicate),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        EventPipelineExpression pipeline = EnsureProjection(Pipeline).AppendFilter(translated);
        return new QueryKernel<TSubject>(Filter, Projection, pipeline);
    }

    public QueryKernel<TSubject> Select(params Expression<Func<TSubject, object?>>[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Select(fields.Select(EventProjectionExpressionTranslator.Translate).ToArray());
    }

    public QueryKernel<TSubject> Select(
        Expression<Func<TSubject, ProjectionContext<TSubject>, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var translated = KernelParameterKeyRewriter.Rebase(
            EventProjectionSelectorTranslator.Translate(selector),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        translated = ProjectedFieldProjection(translated);
        return new QueryKernel<TSubject>(
            Filter,
            Projection
                .WithFields(translated.Fields)
                .WithIncludes(translated.Includes),
            Pipeline.AppendOrMergeLastProjection(translated));
    }

    public QueryKernel<TSubject> Select(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Select(fields.Select(static field => new EventProjectionField(field)).ToArray());
    }

    public QueryKernel<TSubject> Select(params EventProjectionField[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        EventProjectionExpression projection = ProjectedFieldProjection(fields);
        return new QueryKernel<TSubject>(
            Filter,
            Projection.WithFields(projection.Fields),
            Pipeline.AppendOrMergeLastProjection(projection));
    }

    public QueryKernel<TSubject> Include(params EventProjectionInclude[] includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        var projection = KernelParameterKeyRewriter.Rebase(
            EventProjectionExpression.Default.WithIncludes(includes),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        return new QueryKernel<TSubject>(
            Filter,
            Projection.WithIncludes(projection.Includes),
            Pipeline.AppendOrMergeLastProjection(projection));
    }

    private static EventPipelineExpression EnsureProjection(EventPipelineExpression pipeline) =>
        pipeline.HasProjection
            ? pipeline
            : pipeline.AppendProjection(EventProjectionExpression.Default);

    private EventProjectionExpression ProjectedFieldProjection(EventProjectionField[] fields)
    {
        if (!ProjectionWillReadProjectedEvent())
            return EventProjectionExpression.Default.WithFields(fields);

        return EventProjectionExpression.Default.WithFields(
            fields.Select(static field => ProjectedField(field)).ToArray());
    }

    private EventProjectionExpression ProjectedFieldProjection(EventProjectionExpression projection)
    {
        if (!ProjectionWillReadProjectedEvent())
            return projection;

        return projection with
        {
            Fields = projection.Fields
                .Select(static field => ProjectedField(field))
                .ToArray(),
        };
    }

    private static EventProjectionField ProjectedField(EventProjectionField field) =>
        ProjectedEventPaths.TrySplit(field.Path, out _, out _)
            ? field
            : new EventProjectionField(ProjectedEventPaths.Field(field.Path), field.Name);

    private bool ProjectionWillReadProjectedEvent() =>
        ProjectionDomain(Pipeline);

    private static bool ProjectionDomain(EventPipelineExpression pipeline)
    {
        bool currentIsProjected = false;
        bool lastProjectionReadProjected = false;
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            if (pipeline.Stages[i].Kind != EventPipelineStageKind.Projection)
                continue;
            lastProjectionReadProjected = currentIsProjected;
            currentIsProjected = true;
        }

        return pipeline.Stages is [.., { Kind: EventPipelineStageKind.Projection }]
            ? lastProjectionReadProjected
            : currentIsProjected;
    }
}

public static class QueryKernelProjectionExtensions
{
    public static QueryKernel<TSubject> Where<TSubject>(
        this QueryKernel<TSubject> kernel,
        Expression<Func<ProjectedEvent, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.WhereProjected(predicate);
    }
}

public static class QueryKernelPredicates
{
    public static bool In<TValue>(this TValue value, params TValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Contains(value);
    }

    public static bool Exists<TValue>(this TValue? value)
        where TValue : struct =>
        value.HasValue;

    public static bool Exists(this string? value) =>
        value is not null;
}

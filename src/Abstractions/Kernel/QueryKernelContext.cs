using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Translation;

namespace SiftQL;

public sealed record QueryKernel<TSubject, TContext>
{
    private readonly ContextProjectionBinding[] _bindings;

    internal QueryKernel(QueryKernel<TSubject> kernel, IReadOnlyList<ContextProjectionBinding>? bindings = null)
    {
        Kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _bindings = bindings?.ToArray() ?? [];
    }

    public QueryKernel<TSubject> Kernel { get; }
    public FilterExpression Filter => Kernel.Filter;
    public EventProjectionExpression Projection => Kernel.Projection;
    public EventPipelineExpression Pipeline => Kernel.Pipeline;

    public QueryKernel<TSubject, TContext> Where(Expression<Func<TSubject, bool>> predicate) =>
        new(Kernel.Where(predicate), _bindings);

    public QueryKernel<TSubject, TContext> Where(
        Expression<Func<TSubject, TContext, bool>> predicate)
    {
        ContextFilterTranslation sourceTranslated = ContextKernelExpressionTranslator.Translate(
            predicate,
            Pipeline,
            _bindings,
            KernelParameterKeyRewriter.ParameterOffset(Pipeline),
            projectSubjectFields: false);
        if (sourceTranslated.NewIncludes.Length == 0 &&
            !ReferencesContextPath(sourceTranslated.Filter))
        {
            return new QueryKernel<TSubject, TContext>(
                Kernel with { Pipeline = Pipeline.AppendSourceFilter(sourceTranslated.Filter) },
                sourceTranslated.Bindings);
        }

        ContextFilterTranslation translated = ContextKernelExpressionTranslator.Translate(
            predicate,
            Pipeline,
            _bindings,
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        EventPipelineExpression pipeline = ContextProjectionPipeline
            .AddIncludes(Pipeline, translated.NewIncludes)
            .AppendFilter(translated.Filter);
        return new QueryKernel<TSubject, TContext>(
            Kernel with { Pipeline = pipeline },
            translated.Bindings);
    }

    public ProjectedQueryKernel<TSubject, TProjection> Select<TProjection>(
        Expression<Func<TSubject, TContext, TProjection>> selector)
    {
        ContextSelectorTranslation translated = ContextProjectionSelectorTranslator.Translate(
            selector,
            _bindings,
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        EventPipelineExpression pipeline = BuildSelectPipeline(translated);
        return new ProjectedQueryKernel<TSubject, TProjection>(
            Kernel with { Pipeline = pipeline });
    }

    public QueryKernel<TSubject, TContext> Select(params Expression<Func<TSubject, object?>>[] fields) =>
        new(Kernel.Select(fields), _bindings);

    public QueryKernel<TSubject, TContext> Select(params string[] fields) =>
        new(Kernel.Select(fields), _bindings);

    public QueryKernel<TSubject, TContext> Select(params EventProjectionField[] fields) =>
        new(Kernel.Select(fields), _bindings);

    public QueryKernel<TSubject, TContext> Include(params EventProjectionInclude[] includes) =>
        new(Kernel.Include(includes), _bindings);

    public QueryKernel<TSubject> ToQueryKernel() => Kernel;

    public static implicit operator QueryKernel<TSubject>(QueryKernel<TSubject, TContext> kernel) =>
        kernel.Kernel;

    private EventPipelineExpression BuildSelectPipeline(ContextSelectorTranslation translated)
    {
        bool projected = ContextProjectionPipeline.HasProjection(Pipeline);
        if (!projected && translated.NewIncludes.Length == 0)
        {
            EventProjectionExpression projection = EventProjectionExpression.Default.WithFields(
                translated.Outputs.Select(static output =>
                    new EventProjectionField(output.SourcePath, output.Name)).ToArray());
            return Pipeline.AppendOrMergeLastProjection(projection);
        }

        EventProjectionField[] sourceFields = projected
            ? []
            : translated.Outputs
                .Where(static output => !output.IsContext)
                .Select(static output => new EventProjectionField(output.SourcePath, output.Name))
                .ToArray();
        EventPipelineExpression pipeline = ContextProjectionPipeline.AddIncludes(
            Pipeline,
            translated.NewIncludes,
            sourceFields);
        EventProjectionExpression finalProjection = EventProjectionExpression.Default.WithFields(
            translated.Outputs.Select(output => FinalField(output, projected)).ToArray());
        return pipeline.AppendProjection(finalProjection);
    }

    private EventProjectionField FinalField(ContextSelectorOutput output, bool projected)
    {
        if (output.IsContext)
            return new EventProjectionField(output.ProjectedPath, output.Name);

        string fieldName = projected
            ? ContextProjectionPipeline.ProjectedFieldName(Pipeline, output.SourcePath)
            : output.Name;
        return new EventProjectionField(ProjectedEventPaths.Field(fieldName), output.Name);
    }

    private static FilterExpression ToSourceFilter(FilterExpression expression) =>
        expression with
        {
            Field = ProjectedEventPaths.TrySplit(expression.Field, out bool context, out string name) && !context
                ? name
                : expression.Field,
            Children = expression.Children.Select(ToSourceFilter).ToArray(),
        };

    private static bool ReferencesContextPath(FilterExpression expression)
    {
        if (ProjectedEventPaths.TrySplit(expression.Field, out bool context, out _) && context)
            return true;

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (ReferencesContextPath(expression.Children[i]))
                return true;
        }

        return false;
    }
}

public static class QueryKernelContextExtensions
{
    public static QueryKernel<TSubject, TContext> WithContext<TSubject, TContext>(
        this QueryKernel<TSubject> kernel) =>
        new(kernel);
}

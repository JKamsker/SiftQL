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
        SourceFilterSplit split = SplitSourceConjuncts(translated.Filter);
        EventPipelineExpression basePipeline = split.SourceFilter.Kind == FilterExpressionKind.Any
            ? Pipeline
            : Pipeline.AppendSourceFilter(ToSourceFilter(split.SourceFilter));
        RequiredSourceProjection sourceProjection = ContextProjectionPipeline.HasProjection(basePipeline)
            ? RequiredSourceFields(basePipeline, translated.SourceFields)
            : RequiredInitialSourceFields(translated.SourceFields);
        EventPipelineExpression pipeline = ContextProjectionPipeline
            .AddIncludes(
                basePipeline,
                translated.NewIncludes,
                sourceProjection.Fields)
            .AppendFilter(RewriteProjectedSourceFields(split.ContextFilter, sourceProjection.ProjectedPaths));
        return new QueryKernel<TSubject, TContext>(
            Kernel with { Pipeline = pipeline },
            translated.Bindings);
    }

    public ProjectedQueryKernel<TSubject, TProjection> Select<TProjection>(
        Expression<Func<TSubject, TContext, TProjection>> selector)
    {
        ContextSelectorTranslation translated = ContextProjectionSelectorTranslator.Translate(
            selector,
            Pipeline,
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
        if (!projected && sourceFields.Length == 0 && translated.NewIncludes.Length != 0)
            sourceFields = [new EventProjectionField("subjectType", "__siftqlSelectorSource")];

        EventPipelineExpression pipeline = ContextProjectionPipeline.AddIncludes(
            Pipeline,
            translated.NewIncludes,
            sourceFields);
        EventProjectionExpression finalProjection = EventProjectionExpression.Default.WithFields(
            translated.Outputs.Select(output => FinalField(output, projected)).ToArray());
        return pipeline.AppendProjection(finalProjection);
    }

    private static RequiredSourceProjection RequiredSourceFields(
        EventPipelineExpression pipeline,
        IReadOnlyList<string> sourceFields)
    {
        if (sourceFields.Count == 0)
            return RequiredSourceProjection.Empty;

        var fields = new List<EventProjectionField>();
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fieldNames = ProjectedFieldNames(pipeline);
        var projectedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sourceFields.Count; i++)
        {
            string sourcePath = sourceFields[i];
            if (ContextProjectionPipeline.TryProjectedFieldName(pipeline, sourcePath, out _))
                continue;
            if (!sourcePaths.Add(sourcePath))
                continue;

            string fieldName = RequiredSourceFieldName(sourcePath);
            if (!fieldNames.Add(fieldName))
                fieldName = NextSourceFieldName(fieldNames);

            fields.Add(new EventProjectionField(sourcePath, fieldName));
            string defaultPath = ContextProjectionPipeline.ProjectedPath(pipeline, sourcePath);
            string projectedPath = ProjectedEventPaths.Field(fieldName);
            if (!string.Equals(defaultPath, projectedPath, StringComparison.OrdinalIgnoreCase))
                projectedPaths.Add(defaultPath, projectedPath);
        }

        return new RequiredSourceProjection(fields.ToArray(), projectedPaths);
    }

    private static RequiredSourceProjection RequiredInitialSourceFields(
        IReadOnlyList<string> sourceFields)
    {
        string[] required = sourceFields
            .Where(RequiresInitialProjection)
            .ToArray();
        return required.Length == 0
            ? RequiredSourceProjection.Empty
            : RequiredSourceFields(EventPipelineExpression.Default, required);
    }

    private static bool RequiresInitialProjection(string path) =>
        ProjectedEventPaths.TrySplit(path, out _, out _) ||
        string.Equals(path, "subjectType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "subjectName", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "subjectTypes", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".subjectTypes", StringComparison.OrdinalIgnoreCase);

    private static string RequiredSourceFieldName(string sourcePath) =>
        ProjectedEventPaths.TrySplit(sourcePath, out bool context, out string name) && !context
            ? name
            : sourcePath;

    private static HashSet<string> ProjectedFieldNames(EventPipelineExpression pipeline)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            if (pipeline.Stages[i].Kind != EventPipelineStageKind.Projection)
                continue;

            EventProjectionField[] fields = pipeline.Stages[i].Projection.Fields;
            for (int j = 0; j < fields.Length; j++)
                names.Add(fields[j].Name);
            break;
        }

        return names;
    }

    private static string NextSourceFieldName(HashSet<string> names)
    {
        for (int i = 0; ; i++)
        {
            string name = "__siftqlSource" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (names.Add(name))
                return name;
        }
    }

    private static FilterExpression RewriteProjectedSourceFields(
        FilterExpression expression,
        IReadOnlyDictionary<string, string> projectedPaths)
    {
        if (projectedPaths.Count == 0)
            return expression;

        return expression with
        {
            Field = projectedPaths.TryGetValue(expression.Field, out string? projectedPath)
                ? projectedPath
                : expression.Field,
            Children = expression.Children
                .Select(child => RewriteProjectedSourceFields(child, projectedPaths))
                .ToArray(),
        };
    }

    private EventProjectionField FinalField(ContextSelectorOutput output, bool projected)
    {
        if (output.IsContext)
        {
            string path = projected &&
                ContextProjectionPipeline.TryProjectedFieldName(Pipeline, output.ProjectedPath, out string contextFieldName)
                ? ProjectedEventPaths.Field(contextFieldName)
                : output.ProjectedPath;
            return new EventProjectionField(path, output.Name);
        }

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

    private static SourceFilterSplit SplitSourceConjuncts(FilterExpression expression)
    {
        if (expression.Kind != FilterExpressionKind.And)
            return new(FilterExpression.Any, expression);

        var source = new List<FilterExpression>();
        var context = new List<FilterExpression>();
        for (int i = 0; i < expression.Children.Length; i++)
        {
            FilterExpression child = expression.Children[i];
            if (ReferencesContextPath(child))
                context.Add(child);
            else
                source.Add(child);
        }

        return source.Count == 0
            ? new(FilterExpression.Any, expression)
            : new(CombineAnd(source), CombineAnd(context));
    }

    private static FilterExpression CombineAnd(IReadOnlyList<FilterExpression> filters) =>
        filters.Count switch
        {
            0 => FilterExpression.Any,
            1 => filters[0],
            _ => FilterExpression.And(filters.ToArray()),
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

    private sealed record RequiredSourceProjection(
        EventProjectionField[] Fields,
        IReadOnlyDictionary<string, string> ProjectedPaths)
    {
        public static RequiredSourceProjection Empty { get; } =
            new([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed record SourceFilterSplit(
        FilterExpression SourceFilter,
        FilterExpression ContextFilter);
}

public static class QueryKernelContextExtensions
{
    public static QueryKernel<TSubject, TContext> WithContext<TSubject, TContext>(
        this QueryKernel<TSubject> kernel) =>
        new(kernel);
}

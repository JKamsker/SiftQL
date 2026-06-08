using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Translation;

namespace SiftQL;

public sealed record ProjectedQueryKernel<TSource, TProjection>
{
    internal ProjectedQueryKernel(QueryKernel<TSource> kernel)
    {
        Kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public QueryKernel<TSource> Kernel { get; }
    public FilterExpression Filter => Kernel.Filter;
    public EventProjectionExpression Projection => Kernel.Projection;
    public EventPipelineExpression Pipeline => Kernel.Pipeline;

    public ProjectedQueryKernel<TSource, TProjection> Where(
        Expression<Func<TProjection, bool>> predicate)
    {
        FilterExpression translated = KernelParameterKeyRewriter.Rebase(
            ProjectedKernelExpressionTranslator.Translate(predicate),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        return new ProjectedQueryKernel<TSource, TProjection>(
            Kernel with { Pipeline = Pipeline.AppendFilter(translated) });
    }

    public ProjectedQueryKernel<TSource, TNext> Select<TNext>(
        Expression<Func<TProjection, TNext>> selector)
    {
        EventProjectionExpression translated = KernelParameterKeyRewriter.Rebase(
            ProjectedSelectorTranslator.Translate(selector),
            KernelParameterKeyRewriter.ParameterOffset(Pipeline));
        EventPipelineExpression pipeline = Pipeline.AppendProjection(translated);
        return new ProjectedQueryKernel<TSource, TNext>(
            Kernel with { Pipeline = pipeline });
    }

    public QueryKernel<TSource> ToQueryKernel() => Kernel;

    public static implicit operator QueryKernel<TSource>(ProjectedQueryKernel<TSource, TProjection> kernel) =>
        kernel.Kernel;
}

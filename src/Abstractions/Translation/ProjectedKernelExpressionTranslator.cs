using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class ProjectedKernelExpressionTranslator
{
    public static FilterExpression Translate<TProjection>(
        Expression<Func<TProjection, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Rebase(KernelExpressionTranslator.Translate(predicate));
    }

    private static FilterExpression Rebase(FilterExpression expression) =>
        expression with
        {
            Field = string.IsNullOrWhiteSpace(expression.Field)
                ? expression.Field
                : ProjectedEventPaths.Field(expression.Field),
            Children = expression.Children.Select(Rebase).ToArray(),
        };
}

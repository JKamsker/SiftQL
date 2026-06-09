using System.Collections;
using System.Linq.Expressions;
using SiftQL.Expressions;
using static SiftQL.Translation.ExpressionTranslationHelpers;

namespace SiftQL.Translation;

internal static class KernelExpressionValues
{
    public static IReadOnlyCollection<FilterValue> ToValues(
        Expression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        object? value = KernelExpressionEvaluator.Evaluate(expression, parameter);
        if (value is string)
        {
            throw new KernelExpressionException("String constants are scalar values, not filter value lists.");
        }

        if (value is not IEnumerable enumerable)
        {
            throw new KernelExpressionException("Filter value list expression must evaluate to an enumerable.");
        }

        var values = new List<FilterValue>();
        foreach (object? item in enumerable)
        {
            values.Add(FilterValue.FromObject(item) with { ParameterKey = NextParameterKey(ref parameterIndex) });
        }

        return values;
    }

    public static FilterValue ToValue(
        Expression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        Type? targetType = null)
    {
        object? value = KernelExpressionEvaluator.Evaluate(expression, parameter);
        return FilterValue.FromObject(CoerceValue(value, targetType)) with
        {
            ParameterKey = NextParameterKey(ref parameterIndex),
        };
    }

    private static string NextParameterKey(ref int parameterIndex) => "p" + parameterIndex++;
}

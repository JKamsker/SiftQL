using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionShapeValidator
{
    public static void Validate(
        FilterExpression expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ValidateCore(expression, errorFactory);
    }

    private static void ValidateCore(
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Values is null)
            throw Error(errorFactory, "Filter value arrays cannot be null.");
        if (expression.Children is null)
            throw Error(errorFactory, "Filter children cannot be null.");

        for (int i = 0; i < expression.Values.Length; i++)
        {
            if (expression.Values[i] is null)
                throw Error(errorFactory, "Filter value arrays cannot contain null.");
        }

        for (int i = 0; i < expression.Children.Length; i++)
        {
            FilterExpression? child = expression.Children[i];
            if (child is null)
                throw Error(errorFactory, "Filter children cannot contain null.");
            ValidateCore(child, errorFactory);
        }
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

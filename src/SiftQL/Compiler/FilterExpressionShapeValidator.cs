using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionShapeValidator
{
    // Far above any legitimate filter (the compiled engine caps at 16 and the
    // JSON wire format at 64) but low enough that validation never recurses
    // anywhere near stack exhaustion.
    private const int MaxDepth = 256;

    public static void Validate(
        FilterExpression expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ValidateCore(expression, errorFactory, depth: 0);
    }

    private static void ValidateCore(
        FilterExpression expression,
        Func<string, Exception>? errorFactory,
        int depth)
    {
        if (depth > MaxDepth)
            throw Error(errorFactory, $"Filter exceeds the {MaxDepth} level depth limit.");
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
            ValidateCore(child, errorFactory, depth + 1);
        }
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

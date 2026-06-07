using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionParameters
{
    public static bool HasParameters(FilterExpression expression)
    {
        bool found = false;
        VisitValues(expression, value =>
        {
            if (!string.IsNullOrWhiteSpace(value.ParameterKey))
                found = true;
        });
        return found;
    }

    public static string[] Keys(FilterExpression expression)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        VisitValues(expression, value =>
        {
            if (string.IsNullOrWhiteSpace(value.ParameterKey) || !seen.Add(value.ParameterKey))
                return;
            keys.Add(value.ParameterKey);
        });
        return keys.ToArray();
    }

    public static FilterValue[] BindValues(
        FilterExpression expression,
        IReadOnlyList<string> keys)
    {
        var values = new Dictionary<string, FilterValue>(StringComparer.Ordinal);
        VisitValues(expression, value =>
        {
            if (!string.IsNullOrWhiteSpace(value.ParameterKey))
                AddValue(values, value, "Filter");
        });

        var bound = new FilterValue[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            if (!values.TryGetValue(keys[i], out var value))
                throw new FilterValidationException($"Filter parameter '{keys[i]}' is missing.");
            bound[i] = value;
        }

        return bound;
    }

    private static void AddValue(
        Dictionary<string, FilterValue> values,
        FilterValue value,
        string label)
    {
        string key = value.ParameterKey!;
        if (!values.TryGetValue(key, out FilterValue? existing))
        {
            values.Add(key, value);
            return;
        }

        if (!existing.Equals(value))
        {
            throw new FilterValidationException(
                $"{label} parameter '{key}' is used with conflicting values.");
        }
    }

    private static void VisitValues(
        FilterExpression expression,
        Action<FilterValue> visit)
    {
        if (expression.Value is not null)
            visit(expression.Value);
        for (int i = 0; i < expression.Values.Length; i++)
            visit(expression.Values[i]);
        for (int i = 0; i < expression.Children.Length; i++)
            VisitValues(expression.Children[i], visit);
    }
}

using System.Globalization;
using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionParameters
{
    public static bool HasParameters(FilterExpression expression)
    {
        bool hasParameters = false;
        VisitValues(expression, value => hasParameters |= HasParameter(value));
        return hasParameters;
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

        if (!ValuesMatch(existing, value))
        {
            throw new FilterValidationException(
                $"{label} parameter '{key}' is used with conflicting values.");
        }
    }

    private static bool ValuesMatch(FilterValue left, FilterValue right)
    {
        if (left.Kind != right.Kind)
            return false;

        return left.Kind switch
        {
            FilterValueKind.Null => true,
            FilterValueKind.Boolean => left.Boolean == right.Boolean,
            FilterValueKind.Integer => left.Integer == right.Integer,
            FilterValueKind.UnsignedInteger => left.UnsignedInteger == right.UnsignedInteger,
            FilterValueKind.Number => BitConverter.DoubleToInt64Bits(left.Number) ==
                BitConverter.DoubleToInt64Bits(right.Number),
            FilterValueKind.Decimal => left.Decimal == right.Decimal,
            FilterValueKind.String => string.Equals(left.String, right.String, StringComparison.Ordinal),
            FilterValueKind.Guid => left.Guid == right.Guid,
            FilterValueKind.Timestamp => TimestampText(left.Timestamp) == TimestampText(right.Timestamp),
            _ => false,
        };
    }

    private static string TimestampText(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    private static void VisitValues(
        FilterExpression expression,
        Action<FilterValue> visit)
    {
        switch (expression.Kind)
        {
            case FilterExpressionKind.Compare:
            case FilterExpressionKind.Contains:
            case FilterExpressionKind.Count:
                if (expression.Value is not null)
                    visit(expression.Value);
                break;
            case FilterExpressionKind.In:
            case FilterExpressionKind.Between:
                for (int i = 0; i < expression.Values.Length; i++)
                    visit(expression.Values[i]);
                break;
            case FilterExpressionKind.ElemMatch:
            case FilterExpressionKind.And:
            case FilterExpressionKind.Or:
            case FilterExpressionKind.Not:
                for (int i = 0; i < expression.Children.Length; i++)
                    VisitValues(expression.Children[i], visit);
                break;
        }
    }

    private static bool HasParameter(FilterValue? value) =>
        !string.IsNullOrWhiteSpace(value?.ParameterKey);
}

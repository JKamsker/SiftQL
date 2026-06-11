using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Projection;

internal static class ProjectionExpressionParameters
{
    public static bool HasParameters(EventProjectionExpression projection)
    {
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            if (projection.Includes[i]?.Arguments is not { } arguments)
                continue;

            for (int j = 0; j < arguments.Length; j++)
            {
                EventProjectionArgument? argument = arguments[j];
                if (argument?.Kind == EventProjectionArgumentKind.Value &&
                    !string.IsNullOrWhiteSpace(argument.Value?.ParameterKey))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string[] Keys(EventProjectionExpression projection)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            if (projection.Includes[i] is null)
                continue;

            foreach (EventProjectionArgument argument in CanonicalArguments(projection.Includes[i]))
            {
                if (argument.Kind != EventProjectionArgumentKind.Value)
                    continue;

                string? key = argument.Value?.ParameterKey;
                if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                    keys.Add(key);
            }
        }

        return keys.ToArray();
    }

    public static FilterValue[] BindValues(
        EventProjectionExpression projection,
        IReadOnlyList<string> keys)
    {
        var values = new Dictionary<string, FilterValue>(StringComparer.Ordinal);
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            if (projection.Includes[i]?.Arguments is not { } arguments)
                continue;

            for (int j = 0; j < arguments.Length; j++)
            {
                EventProjectionArgument? argument = arguments[j];
                if (argument?.Kind != EventProjectionArgumentKind.Value)
                    continue;

                FilterValue? value = argument.Value;
                if (value is not null && !string.IsNullOrWhiteSpace(value.ParameterKey))
                    AddValue(values, value);
            }
        }

        var bound = new FilterValue[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            if (!values.TryGetValue(keys[i], out var value))
                throw new FilterValidationException($"Projection parameter '{keys[i]}' is missing.");
            bound[i] = value;
        }

        return bound;
    }

    private static IEnumerable<EventProjectionArgument> CanonicalArguments(
        EventProjectionInclude include) =>
        (include.Arguments ?? [])
            .Where(static item => item is not null)
            .OrderBy(static item => item.Name, StringComparer.Ordinal);

    private static void AddValue(
        Dictionary<string, FilterValue> values,
        FilterValue value)
    {
        string key = value.ParameterKey!;
        if (!values.TryGetValue(key, out FilterValue? existing))
        {
            values.Add(key, value);
            return;
        }

        if (!SameValue(existing, value))
        {
            throw new FilterValidationException(
                $"Projection parameter '{key}' is used with conflicting values.");
        }
    }

    private static bool SameValue(FilterValue left, FilterValue right)
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
            FilterValueKind.Timestamp => ProjectionTimestampKey.Equals(left.Timestamp, right.Timestamp),
            _ => false,
        };
    }
}

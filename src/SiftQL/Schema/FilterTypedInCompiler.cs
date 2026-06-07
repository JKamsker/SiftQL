using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Schema;

internal static class FilterTypedInCompiler
{
    private const int UnrolledLimit = 4;

    public static Func<object, bool> CompileBoolean(Func<object, bool?> getter, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        bool hasTrue = values.Any(static value => value.Kind == FilterValueKind.Boolean && value.Boolean);
        bool hasFalse = values.Any(static value => value.Kind == FilterValueKind.Boolean && !value.Boolean);
        return subject => getter(subject) switch
        {
            null => hasNull,
            true => hasTrue,
            false => hasFalse,
        };
    }

    public static Func<object, bool> CompileNumber(Func<object, double?> getter, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        double[] expected = values
            .Where(static value => value.Kind != FilterValueKind.Null)
            .Select(static value => value.Kind switch
            {
                FilterValueKind.Integer => value.Integer,
                FilterValueKind.UnsignedInteger => value.UnsignedInteger,
                FilterValueKind.Decimal => (double)value.Decimal,
                _ => value.Number,
            })
            .Where(static value => !double.IsNaN(value))
            .Distinct()
            .ToArray();
        if (expected.Length <= UnrolledLimit)
            return subject => Match(getter(subject), hasNull, expected);

        var lookup = new HashSet<double>(expected);
        return subject =>
        {
            double? actual = getter(subject);
            return actual.HasValue ? lookup.Contains(actual.Value) : hasNull;
        };
    }

    public static Func<object, bool> CompileString(Func<object, string?> getter, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        string[] expected = values
            .Where(static value => value.Kind == FilterValueKind.String && value.String is not null)
            .Select(static value => value.String!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (expected.Length <= UnrolledLimit)
            return subject => Match(getter(subject), hasNull, expected);

        var lookup = new HashSet<string>(expected, StringComparer.Ordinal);
        return subject =>
        {
            string? actual = getter(subject);
            return actual is null ? hasNull : lookup.Contains(actual);
        };
    }

    public static Func<object, bool> CompileGuid(Func<object, Guid?> getter, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        Guid[] expected = values
            .Where(static value => value.Kind == FilterValueKind.Guid)
            .Select(static value => value.Guid)
            .Distinct()
            .ToArray();
        if (expected.Length <= UnrolledLimit)
            return subject => Match(getter(subject), hasNull, expected);

        var lookup = new HashSet<Guid>(expected);
        return subject =>
        {
            Guid? actual = getter(subject);
            return actual.HasValue ? lookup.Contains(actual.Value) : hasNull;
        };
    }

    public static Func<object, bool> CompileEnum(Func<object, long?> getter, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        long[] expected = values
            .Where(static value => value.Kind == FilterValueKind.Integer)
            .Select(static value => value.Integer)
            .Distinct()
            .ToArray();
        if (expected.Length <= UnrolledLimit)
            return subject => Match(getter(subject), hasNull, expected);

        var lookup = new HashSet<long>(expected);
        return subject =>
        {
            long? actual = getter(subject);
            return actual.HasValue ? lookup.Contains(actual.Value) : hasNull;
        };
    }

    private static bool Match(double? actual, bool hasNull, IReadOnlyList<double> expected)
    {
        if (!actual.HasValue)
            return hasNull;
        double value = actual.Value;
        for (int i = 0; i < expected.Count; i++)
        {
            if (value == expected[i])
                return true;
        }

        return false;
    }

    private static bool Match(string? actual, bool hasNull, IReadOnlyList<string> expected)
    {
        if (actual is null)
            return hasNull;
        for (int i = 0; i < expected.Count; i++)
        {
            if (string.Equals(actual, expected[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool Match(Guid? actual, bool hasNull, IReadOnlyList<Guid> expected)
    {
        if (!actual.HasValue)
            return hasNull;
        Guid value = actual.Value;
        for (int i = 0; i < expected.Count; i++)
        {
            if (value == expected[i])
                return true;
        }

        return false;
    }

    private static bool Match(long? actual, bool hasNull, IReadOnlyList<long> expected)
    {
        if (!actual.HasValue)
            return hasNull;
        long value = actual.Value;
        for (int i = 0; i < expected.Count; i++)
        {
            if (value == expected[i])
                return true;
        }

        return false;
    }

    private static bool HasNull(IEnumerable<FilterValue> values) =>
        values.Any(static value => value.Kind == FilterValueKind.Null);
}

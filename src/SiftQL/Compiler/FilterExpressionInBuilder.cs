using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterExpressionInBuilder
{
    private const int UnrolledLimit = 4;

    private static readonly MethodInfo s_stringEquals = typeof(string).GetMethod(
        nameof(string.Equals),
        [typeof(string), typeof(string), typeof(StringComparison)])!;

    public static Expression? Build(Expression actual, FilterValue[] values)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        if (type == typeof(string))
            return BuildStringIn(actual, values);
        if (type.IsEnum)
            return values.Any(static value => value.Kind == FilterValueKind.String) ||
                Enum.GetUnderlyingType(type) == typeof(ulong)
                ? null
                : BuildEnumIn(actual, values);
        if (type == typeof(bool))
            return BuildBooleanIn(actual, values);
        if (type == typeof(Guid))
            return BuildGuidIn(actual, values);
        return FilterNumeric.IsNumeric(type) ? BuildNumberIn(actual, values) : null;
    }

    private static Expression BuildBooleanIn(Expression actual, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        bool hasTrue = values.Any(static value => value.Kind == FilterValueKind.Boolean && value.Boolean);
        bool hasFalse = values.Any(static value => value.Kind == FilterValueKind.Boolean && !value.Boolean);
        Expression value = Nullable.GetUnderlyingType(actual.Type) is null
            ? actual
            : Expression.Property(actual, nameof(Nullable<bool>.Value));
        Expression result = Expression.Condition(value, Expression.Constant(hasTrue), Expression.Constant(hasFalse));
        return Nullable.GetUnderlyingType(actual.Type) is null
            ? result
            : Expression.Condition(
                Expression.Property(actual, nameof(Nullable<bool>.HasValue)),
                result,
                Expression.Constant(hasNull));
    }

    private static Expression BuildNumberIn(Expression actual, FilterValue[] values)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        if (FilterNumeric.IsExactNumeric(type))
            return BuildExactNumberIn(actual, values);

        if (values.All(static value => value.Kind is FilterValueKind.Integer or FilterValueKind.Null))
        {
            if (FilterNumeric.IsSignedIntegral(type))
            {
                long[] expectedIntegers = values
                    .Where(static value => value.Kind == FilterValueKind.Integer)
                    .Select(static value => value.Integer)
                    .Distinct()
                    .ToArray();
                return BuildValueIn(actual, expectedIntegers, HasNull(values), static item => Expression.Convert(item, typeof(long)));
            }

            if (FilterNumeric.IsUnsignedIntegral(type))
            {
                ulong[] expectedIntegers = values
                    .Where(static value => value.Kind == FilterValueKind.Integer && value.Integer >= 0)
                    .Select(static value => (ulong)value.Integer)
                    .Distinct()
                    .ToArray();
                return BuildValueIn(actual, expectedIntegers, HasNull(values), static item => Expression.Convert(item, typeof(ulong)));
            }

            if (type == typeof(decimal))
            {
                decimal[] expectedDecimals = values
                    .Where(static value => value.Kind == FilterValueKind.Integer)
                    .Select(static value => (decimal)value.Integer)
                    .Distinct()
                    .ToArray();
                return BuildValueIn(actual, expectedDecimals, HasNull(values), convert: null);
            }
        }

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
        return BuildValueIn(actual, expected, HasNull(values), static item => Expression.Convert(item, typeof(double)));
    }

    private static Expression BuildExactNumberIn(Expression actual, FilterValue[] values)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        bool hasNull = HasNull(values);
        if (type == typeof(byte))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, byte.MinValue, byte.MaxValue, static value => (byte)value), hasNull, convert: null);
        if (type == typeof(sbyte))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, sbyte.MinValue, sbyte.MaxValue, static value => (sbyte)value), hasNull, convert: null);
        if (type == typeof(short))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, short.MinValue, short.MaxValue, static value => (short)value), hasNull, convert: null);
        if (type == typeof(ushort))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, ushort.MinValue, ushort.MaxValue, static value => (ushort)value), hasNull, convert: null);
        if (type == typeof(int))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, int.MinValue, int.MaxValue, static value => (int)value), hasNull, convert: null);
        if (type == typeof(uint))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, uint.MinValue, uint.MaxValue, static value => (uint)value), hasNull, convert: null);
        if (type == typeof(long))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, long.MinValue, long.MaxValue, static value => (long)value), hasNull, convert: null);
        if (type == typeof(ulong))
            return BuildValueIn(actual, FilterNumericInValues.Integral(values, ulong.MinValue, ulong.MaxValue, static value => (ulong)value), hasNull, convert: null);

        return BuildValueIn(
            actual,
            FilterNumericInValues.Decimal(values),
            hasNull,
            convert: null);
    }

    private static Expression BuildGuidIn(Expression actual, FilterValue[] values)
    {
        Guid[] expected = values
            .Where(static value => value.Kind == FilterValueKind.Guid)
            .Select(static value => value.Guid)
            .Distinct()
            .ToArray();
        return BuildValueIn(actual, expected, HasNull(values), convert: null);
    }

    private static Expression BuildEnumIn(Expression actual, FilterValue[] values)
    {
        long[] expected = values
            .Where(static value => value.Kind == FilterValueKind.Integer)
            .Select(static value => value.Integer)
            .Distinct()
            .ToArray();
        return BuildValueIn(actual, expected, HasNull(values), static item => Expression.Convert(item, typeof(long)));
    }

    private static Expression BuildStringIn(Expression actual, FilterValue[] values)
    {
        bool hasNull = HasNull(values);
        string[] expected = values
            .Where(static value => value.Kind == FilterValueKind.String && value.String is not null)
            .Select(static value => value.String!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Expression match = expected.Length <= UnrolledLimit
            ? BuildStringChain(actual, expected)
            : BuildLookup(actual, new HashSet<string>(expected, StringComparer.Ordinal));
        return hasNull
            ? Expression.OrElse(Expression.Equal(actual, Expression.Constant(null, typeof(string))), match)
            : match;
    }

    private static Expression BuildValueIn<T>(
        Expression actual,
        IReadOnlyList<T> expected,
        bool hasNull,
        Func<Expression, Expression>? convert)
    {
        Type? nullableType = Nullable.GetUnderlyingType(actual.Type);
        Expression value = nullableType is null ? actual : Expression.Property(actual, nameof(Nullable<int>.Value));
        Expression converted = convert?.Invoke(value) ?? value;
        Expression match = expected.Count <= UnrolledLimit
            ? BuildValueChain(converted, expected)
            : BuildLookup(converted, new HashSet<T>(expected));
        return nullableType is null
            ? match
            : Expression.Condition(
                Expression.Property(actual, nameof(Nullable<int>.HasValue)),
                match,
                Expression.Constant(hasNull));
    }

    private static Expression BuildValueChain<T>(Expression actual, IReadOnlyList<T> expected)
    {
        Expression? match = null;
        for (int i = 0; i < expected.Count; i++)
        {
            Expression equals = Expression.Equal(actual, Expression.Constant(expected[i], actual.Type));
            match = match is null ? equals : Expression.OrElse(match, equals);
        }

        return match ?? Expression.Constant(false);
    }

    private static Expression BuildStringChain(Expression actual, IReadOnlyList<string> expected)
    {
        Expression? match = null;
        for (int i = 0; i < expected.Count; i++)
        {
            Expression equals = Expression.Call(
                s_stringEquals,
                actual,
                Expression.Constant(expected[i], typeof(string)),
                Expression.Constant(StringComparison.Ordinal));
            match = match is null ? equals : Expression.OrElse(match, equals);
        }

        return match ?? Expression.Constant(false);
    }

    private static Expression BuildLookup<T>(Expression actual, HashSet<T> expected) =>
        Expression.Call(
            Expression.Constant(expected),
            typeof(HashSet<T>).GetMethod(nameof(HashSet<T>.Contains), [typeof(T)])!,
            actual);

    private static bool HasNull(IEnumerable<FilterValue> values) =>
        values.Any(static value => value.Kind == FilterValueKind.Null);

}

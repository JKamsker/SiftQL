using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterExpressionScalarBuilder
{
    private static readonly MethodInfo s_stringEquals = typeof(string).GetMethod(
        nameof(string.Equals),
        [typeof(string), typeof(string), typeof(StringComparison)])!;

    public static Expression? BuildCompare(
        Expression actual,
        FilterValue value,
        FilterOperator op)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        if (value.Kind == FilterValueKind.Null)
        {
            Expression isNull = FilterExpressionNull.IsNull(actual);
            return op == FilterOperator.Equal ? isNull : Expression.Not(isNull);
        }

        if (type == typeof(string))
            return BuildStringCompare(actual, value, op);
        if (type.IsEnum)
            return value.Kind == FilterValueKind.Integer ? BuildEnumCompare(actual, value.Integer, op) : null;
        if (type == typeof(bool))
            return BuildValueCompare(actual, Expression.Constant(value.Boolean), op);
        if (type == typeof(Guid))
            return BuildValueCompare(actual, Expression.Constant(value.Guid), op);
        return FilterNumeric.IsNumeric(type) ? BuildNumberCompare(actual, value, op) : null;
    }

    public static Expression? BuildIn(Expression actual, FilterValue[] values)
        => FilterExpressionInBuilder.Build(actual, values);

    private static Expression BuildStringCompare(Expression actual, FilterValue value, FilterOperator op)
    {
        var equals = Expression.Call(
            s_stringEquals,
            actual,
            Expression.Constant(value.String, typeof(string)),
            Expression.Constant(StringComparison.Ordinal));
        return op == FilterOperator.Equal ? equals : Expression.Not(equals);
    }

    private static Expression BuildNumberCompare(Expression actual, FilterValue value, FilterOperator op)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        if (value.Kind == FilterValueKind.Integer)
        {
            if (FilterNumeric.IsSignedIntegral(type))
            {
                return BuildValueCompare(
                    actual,
                    Expression.Constant(value.Integer),
                    op,
                    static item => Expression.Convert(item, typeof(long)));
            }

            if (FilterNumeric.IsUnsignedIntegral(type))
            {
                if (value.Integer < 0)
                    return BuildNegativeIntegerUnsignedCompare(actual, op);

                return BuildValueCompare(
                    actual,
                    Expression.Constant((ulong)value.Integer),
                    op,
                    static item => Expression.Convert(item, typeof(ulong)));
            }

            if (type == typeof(decimal))
            {
                return BuildValueCompare(
                    actual,
                    Expression.Constant((decimal)value.Integer),
                    op,
                    static item => Expression.Convert(item, typeof(decimal)));
            }
        }

        if (value.Kind == FilterValueKind.UnsignedInteger)
            return BuildUnsignedIntegerCompare(actual, value.UnsignedInteger, op);

        if (value.Kind == FilterValueKind.Number && FilterNumeric.IsExactNumeric(type))
        {
            return FilterNumeric.TryDoubleToDecimal(value.Number, out decimal expectedDecimal)
                ? BuildValueCompare(
                    actual,
                    Expression.Constant(expectedDecimal),
                    op,
                    static item => Expression.Convert(item, typeof(decimal)))
                : BuildInvalidNumberCompare(op);
        }

        double expected = value.Kind switch
        {
            FilterValueKind.Integer => value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            _ => value.Number,
        };
        return BuildValueCompare(
            actual,
            Expression.Constant(expected),
            op,
            static item => Expression.Convert(item, typeof(double)));
    }

    private static Expression BuildUnsignedIntegerCompare(
        Expression actual,
        ulong expected,
        FilterOperator op)
    {
        Type type = Nullable.GetUnderlyingType(actual.Type) ?? actual.Type;
        if (FilterNumeric.IsUnsignedIntegral(type))
        {
            return BuildValueCompare(
                actual,
                Expression.Constant(expected),
                op,
                static item => Expression.Convert(item, typeof(ulong)));
        }

        if (FilterNumeric.IsSignedIntegral(type))
        {
            if (expected <= long.MaxValue)
            {
                return BuildValueCompare(
                    actual,
                    Expression.Constant((long)expected),
                    op,
                    static item => Expression.Convert(item, typeof(long)));
            }

            return BuildOutOfRangeSignedCompare(actual, op);
        }

        if (type == typeof(decimal))
        {
            return BuildValueCompare(
                actual,
                Expression.Constant((decimal)expected),
                op,
                static item => Expression.Convert(item, typeof(decimal)));
        }

        return BuildValueCompare(
            actual,
            Expression.Constant((double)expected),
            op,
            static item => Expression.Convert(item, typeof(double)));
    }

    private static Expression BuildEnumCompare(Expression actual, long expected, FilterOperator op) =>
        BuildValueCompare(actual, Expression.Constant(expected), op, static item => Expression.Convert(item, typeof(long)));

    private static Expression BuildValueCompare(
        Expression actual,
        Expression expected,
        FilterOperator op,
        Func<Expression, Expression>? convert = null)
    {
        Type? nullableType = Nullable.GetUnderlyingType(actual.Type);
        if (nullableType is null)
            return CompareValues(convert?.Invoke(actual) ?? actual, expected, op);

        Expression hasValue = Expression.Property(actual, nameof(Nullable<int>.HasValue));
        Expression value = convert?.Invoke(Expression.Property(actual, nameof(Nullable<int>.Value))) ??
            Expression.Property(actual, nameof(Nullable<int>.Value));
        Expression compare = CompareValues(value, expected, op);
        return op == FilterOperator.NotEqual
            ? Expression.OrElse(Expression.Not(hasValue), compare)
            : Expression.AndAlso(hasValue, compare);
    }

    private static Expression CompareValues(Expression actual, Expression expected, FilterOperator op) =>
        op switch
        {
            FilterOperator.Equal => Expression.Equal(actual, expected),
            FilterOperator.NotEqual => Expression.NotEqual(actual, expected),
            FilterOperator.GreaterThan => Expression.GreaterThan(actual, expected),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(actual, expected),
            FilterOperator.LessThan => Expression.LessThan(actual, expected),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(actual, expected),
            _ => Expression.Constant(false),
        };

    private static Expression BuildInvalidNumberCompare(FilterOperator op) =>
        Expression.Constant(op == FilterOperator.NotEqual);

    private static Expression BuildNegativeIntegerUnsignedCompare(
        Expression actual,
        FilterOperator op)
    {
        Type? nullableType = Nullable.GetUnderlyingType(actual.Type);
        Expression hasValue = nullableType is null
            ? Expression.Constant(true)
            : Expression.Property(actual, nameof(Nullable<int>.HasValue));
        return op switch
        {
            FilterOperator.NotEqual =>
                Expression.Constant(true),
            FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual => hasValue,
            _ => Expression.Constant(false),
        };
    }

    private static Expression BuildOutOfRangeSignedCompare(
        Expression actual,
        FilterOperator op)
    {
        Type? nullableType = Nullable.GetUnderlyingType(actual.Type);
        Expression hasValue = nullableType is null
            ? Expression.Constant(true)
            : Expression.Property(actual, nameof(Nullable<int>.HasValue));
        return op switch
        {
            FilterOperator.NotEqual => Expression.Constant(true),
            FilterOperator.LessThan or FilterOperator.LessThanOrEqual => hasValue,
            _ => Expression.Constant(false),
        };
    }

}

using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Schema;

internal static class FilterTypedPredicates
{
    public static Func<object, bool>? TryCompileCompare(
        FilterField field,
        FilterValue value,
        FilterOperator op)
    {
        var scalar = field.ScalarAccessor;
        if (scalar is null)
            return null;

        return scalar.Kind switch
        {
            FilterScalarKind.Boolean => CompileBooleanCompare(scalar.Boolean!, value, op),
            FilterScalarKind.Number => CanUseDoubleAccessor(field.ValueType)
                ? CompileNumberCompare(scalar.Number!, value, op)
                : null,
            FilterScalarKind.String => CompileStringCompare(scalar.Text!, value, op),
            FilterScalarKind.Guid => CompileGuidCompare(scalar.Guid!, value, op),
            FilterScalarKind.Enum => CompileEnumCompare(scalar.Enumeration!, value, op),
            _ => null,
        };
    }

    public static Func<object, bool>? TryCompileIn(FilterField field, FilterValue[] values)
    {
        var scalar = field.ScalarAccessor;
        if (scalar is null)
            return null;

        return scalar.Kind switch
        {
            FilterScalarKind.Boolean => CompileBooleanIn(scalar.Boolean!, values),
            FilterScalarKind.Number => CanUseDoubleAccessor(field.ValueType)
                ? CompileNumberIn(scalar.Number!, values)
                : null,
            FilterScalarKind.String => CompileStringIn(scalar.Text!, values),
            FilterScalarKind.Guid => CompileGuidIn(scalar.Guid!, values),
            FilterScalarKind.Enum => CompileEnumIn(scalar.Enumeration!, values),
            _ => null,
        };
    }

    private static Func<object, bool> CompileBooleanCompare(
        Func<object, bool?> getter,
        FilterValue value,
        FilterOperator op)
    {
        if (value.Kind == FilterValueKind.Null)
            return op switch
            {
                FilterOperator.Equal => subject => !getter(subject).HasValue,
                FilterOperator.NotEqual => subject => getter(subject).HasValue,
                _ => static _ => false,
            };

        bool expected = value.Boolean;
        return op == FilterOperator.Equal
            ? subject => getter(subject) == expected
            : subject => getter(subject) != expected;
    }

    private static Func<object, bool> CompileNumberCompare(
        Func<object, double?> getter,
        FilterValue value,
        FilterOperator op)
    {
        if (value.Kind == FilterValueKind.Null)
            return op switch
            {
                FilterOperator.Equal => subject => !getter(subject).HasValue,
                FilterOperator.NotEqual => subject => getter(subject).HasValue,
                _ => static _ => false,
            };

        double expected = value.Kind switch
        {
            FilterValueKind.Integer => value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            FilterValueKind.Decimal => (double)value.Decimal,
            _ => value.Number,
        };
        return op switch
        {
            FilterOperator.Equal => subject => getter(subject) == expected,
            FilterOperator.NotEqual => subject => getter(subject) != expected,
            FilterOperator.GreaterThan => subject => getter(subject) > expected,
            FilterOperator.GreaterThanOrEqual => subject => getter(subject) >= expected,
            FilterOperator.LessThan => subject => getter(subject) < expected,
            FilterOperator.LessThanOrEqual => subject => getter(subject) <= expected,
            _ => static _ => false,
        };
    }

    private static Func<object, bool> CompileStringCompare(
        Func<object, string?> getter,
        FilterValue value,
        FilterOperator op)
    {
        if (value.Kind == FilterValueKind.Null)
            return op == FilterOperator.Equal
                ? subject => getter(subject) is null
                : subject => getter(subject) is not null;
        if (value.String is null)
            return op == FilterOperator.NotEqual
                ? static _ => true
                : static _ => false;

        string expected = value.String;
        return op == FilterOperator.Equal
            ? subject => string.Equals(getter(subject), expected, StringComparison.Ordinal)
            : subject => !string.Equals(getter(subject), expected, StringComparison.Ordinal);
    }

    private static Func<object, bool> CompileGuidCompare(
        Func<object, Guid?> getter,
        FilterValue value,
        FilterOperator op)
    {
        if (value.Kind == FilterValueKind.Null)
            return op switch
            {
                FilterOperator.Equal => subject => !getter(subject).HasValue,
                FilterOperator.NotEqual => subject => getter(subject).HasValue,
                _ => static _ => false,
            };

        Guid expected = value.Guid;
        return op == FilterOperator.Equal
            ? subject => getter(subject) == expected
            : subject => getter(subject) != expected;
    }

    private static Func<object, bool>? CompileEnumCompare(
        Func<object, long?> getter,
        FilterValue value,
        FilterOperator op)
    {
        if (value.Kind != FilterValueKind.Integer)
            return null;

        long expected = value.Integer;
        return op == FilterOperator.Equal
            ? subject => getter(subject) == expected
            : subject => getter(subject) != expected;
    }

    private static Func<object, bool> CompileBooleanIn(Func<object, bool?> getter, FilterValue[] values)
        => FilterTypedInCompiler.CompileBoolean(getter, values);

    private static Func<object, bool> CompileNumberIn(Func<object, double?> getter, FilterValue[] values)
        => FilterTypedInCompiler.CompileNumber(getter, values);

    private static Func<object, bool> CompileStringIn(Func<object, string?> getter, FilterValue[] values)
        => FilterTypedInCompiler.CompileString(getter, values);

    private static Func<object, bool> CompileGuidIn(Func<object, Guid?> getter, FilterValue[] values)
        => FilterTypedInCompiler.CompileGuid(getter, values);

    private static Func<object, bool>? CompileEnumIn(Func<object, long?> getter, FilterValue[] values)
    {
        if (values.Any(static value => value.Kind == FilterValueKind.String))
            return null;
        return FilterTypedInCompiler.CompileEnum(getter, values);
    }

    private static bool CanUseDoubleAccessor(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(float) ||
            type == typeof(double);
    }
}

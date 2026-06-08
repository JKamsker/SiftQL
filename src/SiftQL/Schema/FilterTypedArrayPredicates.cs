using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Schema;

internal static class FilterTypedArrayPredicates
{
    public static Func<object, bool>? TryCompileContains(FilterField field, FilterValue value)
    {
        var array = field.ArrayAccessor;
        if (array is null)
            return null;

        return array.ElementKind switch
        {
            FilterScalarKind.Boolean when value.Kind == FilterValueKind.Boolean =>
                CompileBooleanContains(array.BooleanContains!, value.Boolean),
            FilterScalarKind.Number when
                value.Kind is (FilterValueKind.Integer or
                    FilterValueKind.UnsignedInteger or
                    FilterValueKind.Number or
                    FilterValueKind.Decimal) &&
                CanUseDoubleExpected(value) &&
                !RequiresExactNumeric(field.ValueType) =>
                CompileNumberContains(array.NumberContains!, value),
            FilterScalarKind.String when value.Kind == FilterValueKind.String &&
                value.String is not null =>
                CompileStringContains(array.TextContains!, value.String),
            FilterScalarKind.Guid when value.Kind == FilterValueKind.Guid =>
                CompileGuidContains(array.GuidContains!, value.Guid),
            _ => null,
        };
    }

    private static Func<object, bool> CompileBooleanContains(
        Func<object, bool, bool> contains,
        bool expected) =>
        subject => contains(subject, expected);

    private static Func<object, bool> CompileNumberContains(
        Func<object, double, bool> contains,
        FilterValue value)
    {
        double expected = value.Kind switch
        {
            FilterValueKind.Integer => value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            _ => value.Number,
        };
        return subject => contains(subject, expected);
    }

    private static Func<object, bool> CompileStringContains(
        Func<object, string?, bool> contains,
        string? expected) =>
        subject => contains(subject, expected);

    private static Func<object, bool> CompileGuidContains(
        Func<object, Guid, bool> contains,
        Guid expected) =>
        subject => contains(subject, expected);

    private static bool RequiresExactNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) ||
            type == typeof(sbyte) ||
            type == typeof(short) ||
            type == typeof(ushort) ||
            type == typeof(int) ||
            type == typeof(uint) ||
            type == typeof(long) ||
            type == typeof(ulong) ||
            type == typeof(decimal);
    }

    private static bool CanUseDoubleExpected(FilterValue value) =>
        value.Kind != FilterValueKind.Decimal;
}

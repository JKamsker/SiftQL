using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Compiler;

internal static class FilterExpressionArrayBuilder
{
    public static Expression? BuildContains(Expression access, FilterValue value)
    {
        MethodInfo? method = ContainsMethod(access.Type, value, out object? expected);
        if (method is null)
            return null;

        ParameterInfo[] parameters = method.GetParameters();
        return Expression.Call(
            method,
            Expression.Convert(access, parameters[0].ParameterType),
            Expression.Constant(expected, parameters[1].ParameterType));
    }

    private static MethodInfo? ContainsMethod(
        Type accessType,
        FilterValue value,
        out object? expected)
    {
        expected = null;
        Type? elementType = accessType.IsArray ? accessType.GetElementType() : null;
        if (elementType is null || Nullable.GetUnderlyingType(elementType) is not null)
            return null;

        string? name = value.Kind switch
        {
            FilterValueKind.Boolean when elementType == typeof(bool) => SetExpected(
                value.Boolean,
                nameof(FilterArrayContains.ContainsBoolean),
                out expected),
            FilterValueKind.String when elementType == typeof(string) => SetExpected(
                value.String,
                nameof(FilterArrayContains.ContainsString),
                out expected),
            FilterValueKind.Guid when elementType == typeof(Guid) => SetExpected(
                value.Guid,
                nameof(FilterArrayContains.ContainsGuid),
                out expected),
            FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number => NumberContainsMethod(elementType, value, out expected),
            _ => null,
        };
        if (name is null)
            return null;

        Type expectedType = expected?.GetType() ??
            value.Kind switch
            {
                FilterValueKind.Boolean => typeof(bool),
                FilterValueKind.String => typeof(string),
                FilterValueKind.Guid => typeof(Guid),
                _ => typeof(double),
            };
        return Method(name, accessType, expectedType);
    }

    private static string? NumberContainsMethod(Type type, FilterValue value, out object? expected)
    {
        if (TryExactNumberContains(type, value, out expected, out string? exactMethod))
            return exactMethod;

        expected = value.Kind switch
        {
            FilterValueKind.Integer => (double)value.Integer,
            FilterValueKind.UnsignedInteger => (double)value.UnsignedInteger,
            _ => value.Number,
        };
        return type == typeof(float) ? nameof(FilterArrayContains.ContainsSingle) :
            type == typeof(double) ? nameof(FilterArrayContains.ContainsDouble) :
            null;
    }

    private static string SetExpected<T>(T value, string method, out object? expected)
    {
        expected = value;
        return method;
    }

    private static bool TryExactNumberContains(
        Type type,
        FilterValue value,
        out object? expected,
        out string? method)
    {
        expected = null;
        method = null;
        if (type == typeof(decimal))
        {
            if (!FilterNumericInValues.TryDecimal(value, out decimal number))
                return false;

            expected = number;
            method = nameof(FilterArrayContains.ContainsDecimalValue);
            return true;
        }

        if (!FilterNumericInValues.TryIntegral(value, Min(type), Max(type), out decimal integral))
            return false;

        expected = ExactExpected(type, integral);
        method = ExactMethod(type);
        return method is not null;
    }

    private static MethodInfo? Method(string name, Type arrayType, Type expectedType) =>
        typeof(FilterArrayContains).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [arrayType, expectedType],
            modifiers: null);

    private static decimal Min(Type type) =>
        type == typeof(byte) ? byte.MinValue :
        type == typeof(sbyte) ? sbyte.MinValue :
        type == typeof(short) ? short.MinValue :
        type == typeof(ushort) ? ushort.MinValue :
        type == typeof(int) ? int.MinValue :
        type == typeof(uint) ? uint.MinValue :
        type == typeof(long) ? long.MinValue :
        type == typeof(ulong) ? ulong.MinValue :
        0;

    private static decimal Max(Type type) =>
        type == typeof(byte) ? byte.MaxValue :
        type == typeof(sbyte) ? sbyte.MaxValue :
        type == typeof(short) ? short.MaxValue :
        type == typeof(ushort) ? ushort.MaxValue :
        type == typeof(int) ? int.MaxValue :
        type == typeof(uint) ? uint.MaxValue :
        type == typeof(long) ? long.MaxValue :
        type == typeof(ulong) ? ulong.MaxValue :
        -1;

    private static object? ExactExpected(Type type, decimal value) =>
        type == typeof(byte) ? (byte)value :
        type == typeof(sbyte) ? (sbyte)value :
        type == typeof(short) ? (short)value :
        type == typeof(ushort) ? (ushort)value :
        type == typeof(int) ? (int)value :
        type == typeof(uint) ? (uint)value :
        type == typeof(long) ? (long)value :
        type == typeof(ulong) ? (ulong)value :
        null;

    private static string? ExactMethod(Type type) =>
        type == typeof(byte) ? nameof(FilterArrayContains.ContainsByteValue) :
        type == typeof(sbyte) ? nameof(FilterArrayContains.ContainsSByteValue) :
        type == typeof(short) ? nameof(FilterArrayContains.ContainsInt16Value) :
        type == typeof(ushort) ? nameof(FilterArrayContains.ContainsUInt16Value) :
        type == typeof(int) ? nameof(FilterArrayContains.ContainsInt32Value) :
        type == typeof(uint) ? nameof(FilterArrayContains.ContainsUInt32Value) :
        type == typeof(long) ? nameof(FilterArrayContains.ContainsInt64Value) :
        type == typeof(ulong) ? nameof(FilterArrayContains.ContainsUInt64Value) :
        null;
}

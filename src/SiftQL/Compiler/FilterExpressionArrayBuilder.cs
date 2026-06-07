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
        MethodInfo? method = ContainsMethod(access.Type, value.Kind);
        if (method is null)
            return null;

        ParameterInfo[] parameters = method.GetParameters();
        Expression expected = ContainsExpected(value, parameters[1].ParameterType);
        return Expression.Call(method, Expression.Convert(access, parameters[0].ParameterType), expected);
    }

    private static MethodInfo? ContainsMethod(Type accessType, FilterValueKind valueKind)
    {
        Type? elementType = accessType.IsArray ? accessType.GetElementType() : null;
        if (elementType is null || Nullable.GetUnderlyingType(elementType) is not null)
            return null;

        string? name = valueKind switch
        {
            FilterValueKind.Boolean when elementType == typeof(bool) => nameof(FilterArrayContains.ContainsBoolean),
            FilterValueKind.String when elementType == typeof(string) => nameof(FilterArrayContains.ContainsString),
            FilterValueKind.Guid when elementType == typeof(Guid) => nameof(FilterArrayContains.ContainsGuid),
            FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number => NumberContainsMethod(elementType),
            _ => null,
        };
        return name is null
            ? null
            : typeof(FilterArrayContains).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
    }

    private static string? NumberContainsMethod(Type type) =>
        type == typeof(byte) ? nameof(FilterArrayContains.ContainsByte) :
        type == typeof(sbyte) ? nameof(FilterArrayContains.ContainsSByte) :
        type == typeof(short) ? nameof(FilterArrayContains.ContainsInt16) :
        type == typeof(ushort) ? nameof(FilterArrayContains.ContainsUInt16) :
        type == typeof(int) ? nameof(FilterArrayContains.ContainsInt32) :
        type == typeof(uint) ? nameof(FilterArrayContains.ContainsUInt32) :
        type == typeof(long) ? null :
        type == typeof(ulong) ? null :
        type == typeof(float) ? nameof(FilterArrayContains.ContainsSingle) :
        type == typeof(double) ? nameof(FilterArrayContains.ContainsDouble) :
        type == typeof(decimal) ? null :
        null;

    private static Expression ContainsExpected(FilterValue value, Type parameterType) =>
        parameterType == typeof(bool) ? Expression.Constant(value.Boolean) :
        parameterType == typeof(string) ? Expression.Constant(value.String, typeof(string)) :
        parameterType == typeof(Guid) ? Expression.Constant(value.Guid) :
        Expression.Constant(value.Kind switch
        {
            FilterValueKind.Integer => (double)value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            _ => value.Number,
        });
}

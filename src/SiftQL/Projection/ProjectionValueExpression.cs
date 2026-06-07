using System.Linq.Expressions;
using SiftQL;
using SiftQL.Projected;

namespace SiftQL;

internal static class ProjectionValueExpression
{
    public static Func<object, ProjectedEventValue>? CompileAccessor(
        Type valueType,
        Expression value,
        ParameterExpression parameter)
    {
        var expression = TryBuild(valueType, value);
        return expression is null
            ? null
            : Expression.Lambda<Func<object, ProjectedEventValue>>(expression, parameter).Compile();
    }

    public static Expression? TryBuild(Type valueType, Expression value)
    {
        string? methodName = ProjectionMethodName(valueType);
        if (methodName is null)
            return null;

        bool nullable = Nullable.GetUnderlyingType(value.Type) is not null;
        var method = ProjectionMethod(methodName, valueType, nullable);
        if (valueType.IsEnum)
            method = method.MakeGenericMethod(valueType);

        return Expression.Call(method, ToArgument(value, method.GetParameters()[0].ParameterType));
    }

    private static string? ProjectionMethodName(Type valueType)
    {
        if (valueType == typeof(bool)) return nameof(ProjectionValueFactory.FromBoolean);
        if (valueType == typeof(byte)) return nameof(ProjectionValueFactory.FromByte);
        if (valueType == typeof(sbyte)) return nameof(ProjectionValueFactory.FromSByte);
        if (valueType == typeof(short)) return nameof(ProjectionValueFactory.FromInt16);
        if (valueType == typeof(ushort)) return nameof(ProjectionValueFactory.FromUInt16);
        if (valueType == typeof(int)) return nameof(ProjectionValueFactory.FromInt32);
        if (valueType == typeof(uint)) return nameof(ProjectionValueFactory.FromUInt32);
        if (valueType == typeof(long)) return nameof(ProjectionValueFactory.FromInt64);
        if (valueType == typeof(ulong)) return nameof(ProjectionValueFactory.FromUInt64);
        if (valueType == typeof(float)) return nameof(ProjectionValueFactory.FromSingle);
        if (valueType == typeof(double)) return nameof(ProjectionValueFactory.FromDouble);
        if (valueType == typeof(decimal)) return nameof(ProjectionValueFactory.FromDecimal);
        if (valueType == typeof(string)) return nameof(ProjectionValueFactory.FromString);
        if (valueType == typeof(Guid)) return nameof(ProjectionValueFactory.FromGuid);
        if (valueType.IsEnum) return nameof(ProjectionValueFactory.FromEnum);
        return null;
    }

    private static System.Reflection.MethodInfo ProjectionMethod(
        string methodName,
        Type valueType,
        bool nullable)
    {
        foreach (var method in typeof(ProjectionValueFactory).GetMethods())
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                method.GetParameters().Length != 1)
            {
                continue;
            }

            var parameterType = method.GetParameters()[0].ParameterType;
            if (valueType.IsEnum)
            {
                bool acceptsNullableEnum = parameterType.IsGenericType &&
                    parameterType.GetGenericTypeDefinition() == typeof(Nullable<>);
                if (acceptsNullableEnum == nullable)
                    return method;
                continue;
            }

            Type expected = nullable ? typeof(Nullable<>).MakeGenericType(valueType) : valueType;
            if (parameterType == expected)
                return method;
        }

        throw new MissingMethodException(typeof(ProjectionValueFactory).FullName, methodName);
    }

    private static Expression ToArgument(Expression expression, Type parameterType) =>
        parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is not null
            ? ToNullable(expression, parameterType)
            : Expression.Convert(expression, parameterType);

    private static Expression ToNullable(Expression expression, Type nullableTargetType)
    {
        Type? nullableSource = Nullable.GetUnderlyingType(expression.Type);
        if (nullableSource is null)
        {
            return Expression.Convert(
                Expression.Convert(expression, nullableTargetType.GetGenericArguments()[0]),
                nullableTargetType);
        }

        var hasValue = Expression.Property(expression, nameof(Nullable<int>.HasValue));
        var value = Expression.Property(expression, nameof(Nullable<int>.Value));
        var converted = Expression.Convert(value, nullableTargetType.GetGenericArguments()[0]);
        return Expression.Condition(
            hasValue,
            Expression.Convert(converted, nullableTargetType),
            Expression.Default(nullableTargetType));
    }
}

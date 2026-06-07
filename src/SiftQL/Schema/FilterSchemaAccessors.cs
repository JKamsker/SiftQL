using System.Linq.Expressions;
using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Schema;

internal static class FilterSchemaAccessors
{
    public static FilterScalarAccessor? BuildScalar(
        Type valueType,
        Expression propertyExpression,
        ParameterExpression parameter)
    {
        bool nullable = Nullable.GetUnderlyingType(propertyExpression.Type) is not null;
        if (valueType == typeof(bool))
            return nullable
                ? Scalar(FilterScalarKind.Boolean, boolean: CompileNullable<bool>(propertyExpression, parameter))
                : Scalar(FilterScalarKind.Boolean, requiredBoolean: CompileRequired<bool>(propertyExpression, parameter));
        if (IsNumeric(valueType))
            return nullable
                ? Scalar(FilterScalarKind.Number, number: CompileNullable<double>(propertyExpression, parameter))
                : Scalar(
                    FilterScalarKind.Number,
                    requiredNumber: CompileRequired<double>(
                        propertyExpression,
                        parameter,
                        static item => Expression.Convert(item, typeof(double))));
        if (valueType == typeof(string))
            return Scalar(FilterScalarKind.String, text: Expression.Lambda<Func<object, string?>>(propertyExpression, parameter).Compile());
        if (valueType == typeof(Guid))
            return nullable
                ? Scalar(FilterScalarKind.Guid, guid: CompileNullable<Guid>(propertyExpression, parameter))
                : Scalar(FilterScalarKind.Guid, requiredGuid: CompileRequired<Guid>(propertyExpression, parameter));
        if (valueType.IsEnum)
        {
            if (Enum.GetUnderlyingType(valueType) == typeof(ulong))
                return null;

            return nullable
                ? Scalar(FilterScalarKind.Enum, enumeration: CompileEnum(propertyExpression, parameter))
                : Scalar(
                    FilterScalarKind.Enum,
                    requiredEnumeration: CompileRequired<long>(
                        propertyExpression,
                        parameter,
                        static item => Expression.Convert(item, typeof(long))));
        }

        return null;
    }

    public static FilterArrayAccessor? BuildArray(
        Type elementType,
        Expression propertyExpression,
        ParameterExpression parameter)
    {
        if (!propertyExpression.Type.IsArray)
            return null;
        if (Nullable.GetUnderlyingType(propertyExpression.Type.GetElementType()!) is not null)
            return null;

        if (elementType == typeof(bool))
            return Array(FilterScalarKind.Boolean, booleanContains: CompileArrayContains<bool>(propertyExpression, parameter, nameof(FilterArrayContains.ContainsBoolean)));
        if (IsNumeric(elementType))
            return Array(FilterScalarKind.Number, numberContains: CompileNumberArrayContains(elementType, propertyExpression, parameter));
        if (elementType == typeof(string))
            return Array(FilterScalarKind.String, textContains: CompileArrayContains<string?>(propertyExpression, parameter, nameof(FilterArrayContains.ContainsString)));
        if (elementType == typeof(Guid))
            return Array(FilterScalarKind.Guid, guidContains: CompileArrayContains<Guid>(propertyExpression, parameter, nameof(FilterArrayContains.ContainsGuid)));

        return null;
    }

    public static Func<object, ProjectedEventValue>? BuildProjection(
        Type valueType,
        Expression propertyExpression,
        ParameterExpression parameter) =>
        ProjectionValueExpression.CompileAccessor(valueType, propertyExpression, parameter);

    public static Func<object, ProjectedEventValue> BuildObjectProjection(
        Expression propertyExpression,
        ParameterExpression parameter) =>
        Expression.Lambda<Func<object, ProjectedEventValue>>(
            Expression.Call(
                typeof(ProjectedEventValue).GetMethod(nameof(ProjectedEventValue.FromObject))!,
                Expression.Convert(propertyExpression, typeof(object))),
            parameter).Compile();

    private static FilterScalarAccessor Scalar(
        FilterScalarKind kind,
        Func<object, bool?>? boolean = null,
        Func<object, double?>? number = null,
        Func<object, string?>? text = null,
        Func<object, Guid?>? guid = null,
        Func<object, long?>? enumeration = null,
        Func<object, bool>? requiredBoolean = null,
        Func<object, double>? requiredNumber = null,
        Func<object, Guid>? requiredGuid = null,
        Func<object, long>? requiredEnumeration = null) =>
        new(
            kind,
            boolean,
            number,
            text,
            guid,
            enumeration,
            requiredBoolean,
            requiredNumber,
            requiredGuid,
            requiredEnumeration);

    private static FilterArrayAccessor Array(
        FilterScalarKind kind,
        Func<object, bool, bool>? booleanContains = null,
        Func<object, double, bool>? numberContains = null,
        Func<object, string?, bool>? textContains = null,
        Func<object, Guid, bool>? guidContains = null) =>
        new(kind, booleanContains, numberContains, textContains, guidContains);

    private static Func<object, T?> CompileNullable<T>(
        Expression propertyExpression,
        ParameterExpression parameter)
        where T : struct =>
        Expression.Lambda<Func<object, T?>>(
            ToNullable(propertyExpression, typeof(T?)),
            parameter).Compile();

    private static Func<object, T> CompileRequired<T>(
        Expression propertyExpression,
        ParameterExpression parameter,
        Func<Expression, Expression>? convert = null) =>
        Expression.Lambda<Func<object, T>>(
            convert?.Invoke(propertyExpression) ?? propertyExpression,
            parameter).Compile();

    private static Func<object, long?> CompileEnum(
        Expression propertyExpression,
        ParameterExpression parameter) =>
        Expression.Lambda<Func<object, long?>>(ToEnumNullableLong(propertyExpression), parameter).Compile();

    private static Func<object, TExpected, bool> CompileArrayContains<TExpected>(
        Expression propertyExpression,
        ParameterExpression parameter,
        string methodName)
    {
        var expected = Expression.Parameter(typeof(TExpected), "expected");
        var method = typeof(FilterArrayContains).GetMethod(methodName, [propertyExpression.Type, typeof(TExpected)])!;
        return Expression.Lambda<Func<object, TExpected, bool>>(
            Expression.Call(method, propertyExpression, expected),
            parameter,
            expected).Compile();
    }

    private static Func<object, double, bool> CompileNumberArrayContains(
        Type elementType,
        Expression propertyExpression,
        ParameterExpression parameter)
    {
        var expected = Expression.Parameter(typeof(double), "expected");
        var method = typeof(FilterArrayContains).GetMethod(
            NumberContainsMethodName(elementType),
            [propertyExpression.Type, typeof(double)])!;
        return Expression.Lambda<Func<object, double, bool>>(
            Expression.Call(method, propertyExpression, expected),
            parameter,
            expected).Compile();
    }

    private static string NumberContainsMethodName(Type elementType)
    {
        if (elementType == typeof(byte)) return nameof(FilterArrayContains.ContainsByte);
        if (elementType == typeof(sbyte)) return nameof(FilterArrayContains.ContainsSByte);
        if (elementType == typeof(short)) return nameof(FilterArrayContains.ContainsInt16);
        if (elementType == typeof(ushort)) return nameof(FilterArrayContains.ContainsUInt16);
        if (elementType == typeof(int)) return nameof(FilterArrayContains.ContainsInt32);
        if (elementType == typeof(uint)) return nameof(FilterArrayContains.ContainsUInt32);
        if (elementType == typeof(long)) return nameof(FilterArrayContains.ContainsInt64);
        if (elementType == typeof(ulong)) return nameof(FilterArrayContains.ContainsUInt64);
        if (elementType == typeof(float)) return nameof(FilterArrayContains.ContainsSingle);
        if (elementType == typeof(double)) return nameof(FilterArrayContains.ContainsDouble);
        if (elementType == typeof(decimal)) return nameof(FilterArrayContains.ContainsDecimal);
        throw new ArgumentOutOfRangeException(nameof(elementType), elementType.FullName, null);
    }

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

    private static Expression ToEnumNullableLong(Expression expression)
    {
        Type? nullableSource = Nullable.GetUnderlyingType(expression.Type);
        if (nullableSource is null)
            return Expression.Convert(Expression.Convert(expression, typeof(long)), typeof(long?));

        var hasValue = Expression.Property(expression, nameof(Nullable<int>.HasValue));
        var value = Expression.Property(expression, nameof(Nullable<int>.Value));
        return Expression.Condition(
            hasValue,
            Expression.Convert(Expression.Convert(value, typeof(long)), typeof(long?)),
            Expression.Default(typeof(long?)));
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal);
}

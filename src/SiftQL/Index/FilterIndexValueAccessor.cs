using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Index;

internal static class FilterIndexValueAccessor<TSubject>
{
    private static readonly MethodInfo s_boolean =
        Method(nameof(FilterIndexValue.ForBoolean), typeof(bool));
    private static readonly MethodInfo s_nullableBoolean =
        Method(nameof(FilterIndexValue.ForBoolean), typeof(bool?));
    private static readonly MethodInfo s_number =
        Method(nameof(FilterIndexValue.ForNumber), typeof(double));
    private static readonly MethodInfo s_nullableNumber =
        Method(nameof(FilterIndexValue.ForNumber), typeof(double?));
    private static readonly MethodInfo s_integer =
        Method(nameof(FilterIndexValue.ForInteger), typeof(long));
    private static readonly MethodInfo s_nullableInteger =
        Method(nameof(FilterIndexValue.ForInteger), typeof(long?));
    private static readonly MethodInfo s_string =
        Method(nameof(FilterIndexValue.ForString), typeof(string));
    private static readonly MethodInfo s_guid =
        Method(nameof(FilterIndexValue.ForGuid), typeof(Guid));
    private static readonly MethodInfo s_nullableGuid =
        Method(nameof(FilterIndexValue.ForGuid), typeof(Guid?));
    private static readonly MethodInfo s_enum =
        Method(nameof(FilterIndexValue.ForEnum), typeof(long));
    private static readonly MethodInfo s_nullableEnum =
        Method(nameof(FilterIndexValue.ForEnum), typeof(long?));

    public static Func<TSubject, FilterIndexValue?> Create(FilterField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (TryCreateConstant(field, out var constant))
            return _ => constant;

        var parameter = Expression.Parameter(typeof(TSubject), "subject");
        string? propertyPath = field.Access?.PropertyPath;
        bool hasPropertyPath = propertyPath is not null;
        Expression? value = propertyPath is not null
            ? FilterFieldAccessExpression.Build(parameter, propertyPath)
            : null;
        Expression? key = value is null || RequiresRuntimeNumericNormalization(field, value.Type)
            ? null
            : BuildKeyExpression(field, value);
        if (key is not null)
        {
            if (key.Type == typeof(FilterIndexValue))
                key = Expression.Convert(key, typeof(FilterIndexValue?));
            return Expression.Lambda<Func<TSubject, FilterIndexValue?>>(key, parameter).Compile();
        }

        if (!hasPropertyPath &&
            field.ScalarAccessor is { } scalarAccessor &&
            !PrefersExactNumericGetter(field, scalarAccessor))
        {
            return subject => FilterIndexValue.TryCreateActual(scalarAccessor, subject!, out var actual)
                ? actual
                : TryCreateFromGetter(field, subject);
        }

        return subject => TryCreateFromGetter(field, subject);
    }

    private static FilterIndexValue? TryCreateFromGetter(FilterField field, TSubject subject) =>
        TryCreateActual(field, field.Getter(subject!), out var actual)
            ? actual
            : null;

    private static bool TryCreateActual(
        FilterField field,
        object? value,
        out FilterIndexValue key)
    {
        key = default;
        Type fieldType = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        if (fieldType == typeof(decimal))
            return false;
        if (fieldType.IsEnum)
            return FilterIndexValue.TryCreateActual(value, out key);
        if (FilterNumeric.IsSignedIntegral(fieldType))
            return TryCreateSignedIntegralActual(value, out key);
        if (FilterNumeric.IsUnsignedIntegral(fieldType))
            return TryCreateUnsignedIntegralActual(fieldType, value, out key);
        if (IsFloating(fieldType))
            return TryCreateFloatingActual(value, out key);

        return FilterIndexValue.TryCreateActual(value, out key);
    }

    private static bool TryCreateSignedIntegralActual(object? value, out FilterIndexValue key)
    {
        key = default;
        if (TryGetFloating(value, out double number))
        {
            if (!FilterNumeric.TryDoubleToInt64(number, out long integer))
                return false;

            key = FilterIndexValue.ForInteger(integer);
            return true;
        }

        return FilterIndexValue.TryCreateActual(value, out key) &&
            key.Kind == FilterValueKind.Integer;
    }

    private static bool TryCreateUnsignedIntegralActual(
        Type fieldType,
        object? value,
        out FilterIndexValue key)
    {
        key = default;
        if (TryGetFloating(value, out double number))
        {
            if (!FilterNumeric.TryDoubleToUInt64(number, out ulong integer))
                return false;

            if (integer <= long.MaxValue)
            {
                key = FilterIndexValue.ForInteger((long)integer);
                return true;
            }

            if (fieldType != typeof(ulong))
                return false;

            key = FilterIndexValue.ForUnsignedInteger(integer);
            return true;
        }

        return FilterIndexValue.TryCreateActual(value, out key) &&
            key.Kind is FilterValueKind.Integer or FilterValueKind.UnsignedInteger;
    }

    private static bool TryCreateFloatingActual(object? value, out FilterIndexValue key)
    {
        key = default;
        if (!FilterNumericComparison.TryNumber(value, out double number))
            return false;

        key = FilterIndexValue.ForNumber(number);
        return true;
    }

    private static bool PrefersExactNumericGetter(
        FilterField field,
        FilterScalarAccessor scalarAccessor)
    {
        if (scalarAccessor.Kind != FilterScalarKind.Number)
            return false;

        Type valueType = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        return FilterNumeric.IsExactNumeric(valueType);
    }

    private static bool TryCreateConstant(FilterField field, out FilterIndexValue? key)
    {
        if (field.Access?.PropertyPath is not null)
        {
            key = null;
            return false;
        }

        if (field.Access is not null &&
            TryCreateActual(field, field.Access.ConstantValue, out var value))
        {
            key = value;
            return true;
        }

        key = null;
        return false;
    }

    private static Expression? BuildKeyExpression(FilterField field, Expression value)
    {
        bool nullable = Nullable.GetUnderlyingType(value.Type) is not null;
        return field.ScalarAccessor?.Kind switch
        {
            FilterScalarKind.Boolean => Expression.Call(
                nullable ? s_nullableBoolean : s_boolean,
                nullable ? value : Expression.Convert(value, typeof(bool))),
            FilterScalarKind.Number when IsULong(value.Type) => null,
            FilterScalarKind.Number => Expression.Call(
                NumberMethod(value.Type, nullable),
                ConvertNumber(value, nullable)),
            FilterScalarKind.String => Expression.Call(s_string, value),
            FilterScalarKind.Guid => Expression.Call(
                nullable ? s_nullableGuid : s_guid,
                nullable ? value : Expression.Convert(value, typeof(Guid))),
            FilterScalarKind.Enum when IsULongBackedEnum(value.Type) => null,
            FilterScalarKind.Enum => Expression.Call(
                nullable ? s_nullableEnum : s_enum,
                ConvertEnum(value, nullable)),
            _ => null,
        };
    }

    private static MethodInfo NumberMethod(Type type, bool nullable)
    {
        Type valueType = Nullable.GetUnderlyingType(type) ?? type;
        return IsIntegral(valueType)
            ? nullable ? s_nullableInteger : s_integer
            : nullable ? s_nullableNumber : s_number;
    }

    private static Expression ConvertNumber(Expression value, bool nullable)
    {
        Type valueType = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        Type target = IsIntegral(valueType) ? typeof(long) : typeof(double);
        return nullable
            ? ConvertNullableValue(value, target)
            : Expression.Convert(value, target);
    }

    private static bool IsULong(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) == typeof(ulong);

    private static bool IsULongBackedEnum(Type type)
    {
        Type valueType = Nullable.GetUnderlyingType(type) ?? type;
        return valueType.IsEnum && Enum.GetUnderlyingType(valueType) == typeof(ulong);
    }

    private static bool RequiresRuntimeNumericNormalization(
        FilterField field,
        Type actualType)
    {
        if (field.ScalarAccessor?.Kind != FilterScalarKind.Number)
            return false;

        Type fieldType = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        Type valueType = Nullable.GetUnderlyingType(actualType) ?? actualType;
        return IsFloating(fieldType) != IsFloating(valueType);
    }

    private static bool IsFloating(Type type) =>
        type == typeof(float) ||
        type == typeof(double);

    private static bool TryGetFloating(object? value, out double number)
    {
        switch (value)
        {
            case float item:
                number = item;
                return true;
            case double item:
                number = item;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsIntegral(Type type) =>
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long);

    private static Expression ConvertEnum(Expression value, bool nullable) =>
        nullable
            ? ConvertNullableValue(value, typeof(long))
            : Expression.Convert(value, typeof(long));

    private static Expression ConvertNullableValue(Expression value, Type targetType)
    {
        var hasValue = Expression.Property(value, nameof(Nullable<int>.HasValue));
        var inner = Expression.Property(value, nameof(Nullable<int>.Value));
        var converted = Expression.Convert(inner, targetType);
        Type nullableTarget = typeof(Nullable<>).MakeGenericType(targetType);
        return Expression.Condition(
            hasValue,
            Expression.Convert(converted, nullableTarget),
            Expression.Default(nullableTarget));
    }

    private static MethodInfo Method(string name, Type parameterType) =>
        typeof(FilterIndexValue).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [parameterType],
            modifiers: null)!;
}

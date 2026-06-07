using System.Linq.Expressions;
using MessagePack;
using SiftQL.Schema;

namespace SiftQL.Projection;

internal static class ProjectionPayloadWriterCompiler
{
    public static ProjectedFieldPayloadWriter? TryCompile(
        Type subjectType,
        string projectedName,
        FilterField field)
    {
        if (field.Kind != FilterFieldKind.Scalar ||
            field.Access?.PropertyPath is not { } path)
        {
            return null;
        }

        var subject = Expression.Parameter(typeof(object), "subject");
        Expression? access = BuildPropertyExpression(Expression.Convert(subject, subjectType), path);
        if (access is null)
            return null;

        Type valueType = Nullable.GetUnderlyingType(access.Type) ?? access.Type;
        bool nullable = Nullable.GetUnderlyingType(access.Type) is not null;
        if (valueType == typeof(bool))
            return nullable
                ? NullableBoolean(projectedName, CompileNullable<bool>(access, subject))
                : Boolean(projectedName, CompileRequired<bool>(access, subject));
        if (IsSignedInteger(valueType))
            return nullable
                ? NullableInteger(projectedName, CompileNullable<long>(access, subject, ToLong))
                : Integer(projectedName, CompileRequired<long>(access, subject, ToLong));
        if (IsUnsignedInteger(valueType))
            return nullable
                ? NullableUInt64(projectedName, CompileNullable<ulong>(access, subject, ToUInt64))
                : UInt64(projectedName, CompileRequired<ulong>(access, subject, ToUInt64));
        if (valueType == typeof(float) || valueType == typeof(double))
            return nullable
                ? NullableNumber(projectedName, CompileNullable<double>(access, subject, ToDouble))
                : Number(projectedName, CompileRequired<double>(access, subject, ToDouble));
        if (valueType == typeof(decimal))
            return nullable
                ? NullableDecimal(projectedName, CompileNullable<decimal>(access, subject))
                : Decimal(projectedName, CompileRequired<decimal>(access, subject));
        if (valueType == typeof(string))
            return String(projectedName, Expression.Lambda<Func<object, string?>>(access, subject).Compile());
        if (valueType == typeof(Guid))
            return nullable
                ? NullableGuid(projectedName, CompileNullable<Guid>(access, subject))
                : Guid(projectedName, CompileRequired<Guid>(access, subject));

        return null;
    }

    private static Expression? BuildPropertyExpression(Expression subject, string propertyPath)
    {
        Expression current = subject;
        foreach (string part in propertyPath.Split('.'))
        {
            var property = current.Type.GetProperty(
                part,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (property?.GetMethod is null || property.GetMethod.GetParameters().Length != 0)
                return null;

            current = Expression.Property(current, property);
        }

        return current;
    }

    private static Func<object, T> CompileRequired<T>(
        Expression access,
        ParameterExpression subject,
        Func<Expression, Expression>? convert = null) =>
        Expression.Lambda<Func<object, T>>(convert?.Invoke(access) ?? access, subject).Compile();

    private static Func<object, T?> CompileNullable<T>(
        Expression access,
        ParameterExpression subject,
        Func<Expression, Expression>? convert = null)
        where T : struct
    {
        Type targetType = typeof(T);
        Type nullableTargetType = typeof(T?);
        Type? nullableSource = Nullable.GetUnderlyingType(access.Type);
        if (nullableSource is null)
        {
            Expression converted = convert?.Invoke(access) ?? Expression.Convert(access, targetType);
            return Expression.Lambda<Func<object, T?>>(
                Expression.Convert(converted, nullableTargetType),
                subject).Compile();
        }

        Expression hasValue = Expression.Property(access, nameof(Nullable<int>.HasValue));
        Expression value = Expression.Property(access, nameof(Nullable<int>.Value));
        Expression nullableValue = Expression.Convert(convert?.Invoke(value) ?? value, nullableTargetType);
        return Expression.Lambda<Func<object, T?>>(
            Expression.Condition(hasValue, nullableValue, Expression.Default(nullableTargetType)),
            subject).Compile();
    }

    private static ProjectedFieldPayloadWriter Boolean(string name, Func<object, bool> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            ProjectedPayloadWriter.WriteBooleanField(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableBoolean(string name, Func<object, bool?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            bool? value = read(subject);
            if (value.HasValue)
                ProjectedPayloadWriter.WriteBooleanField(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static ProjectedFieldPayloadWriter Integer(string name, Func<object, long> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            ProjectedPayloadWriter.WriteIntegerField(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableInteger(string name, Func<object, long?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            long? value = read(subject);
            if (value.HasValue)
                ProjectedPayloadWriter.WriteIntegerField(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static ProjectedFieldPayloadWriter UInt64(string name, Func<object, ulong> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            WriteUInt64(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableUInt64(string name, Func<object, ulong?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            ulong? value = read(subject);
            if (value.HasValue)
                WriteUInt64(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static ProjectedFieldPayloadWriter Number(string name, Func<object, double> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            ProjectedPayloadWriter.WriteNumberField(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableNumber(string name, Func<object, double?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            double? value = read(subject);
            if (value.HasValue)
                ProjectedPayloadWriter.WriteNumberField(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static ProjectedFieldPayloadWriter Decimal(string name, Func<object, decimal> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            WriteDecimal(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableDecimal(string name, Func<object, decimal?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            decimal? value = read(subject);
            if (value.HasValue)
                WriteDecimal(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static ProjectedFieldPayloadWriter String(string name, Func<object, string?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            string? value = read(subject);
            if (value is null)
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
            else
                ProjectedPayloadWriter.WriteStringField(ref writer, name, value, options);
        };

    private static ProjectedFieldPayloadWriter Guid(string name, Func<object, Guid> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
            ProjectedPayloadWriter.WriteGuidField(ref writer, name, read(subject), options);

    private static ProjectedFieldPayloadWriter NullableGuid(string name, Func<object, Guid?> read) =>
        (ref MessagePackWriter writer, object subject, MessagePackSerializerOptions options) =>
        {
            Guid? value = read(subject);
            if (value.HasValue)
                ProjectedPayloadWriter.WriteGuidField(ref writer, name, value.Value, options);
            else
                ProjectedPayloadWriter.WriteNullField(ref writer, name, options);
        };

    private static void WriteUInt64(
        ref MessagePackWriter writer,
        string name,
        ulong value,
        MessagePackSerializerOptions options)
    {
        if (value <= long.MaxValue)
            ProjectedPayloadWriter.WriteIntegerField(ref writer, name, (long)value, options);
        else
            ProjectedPayloadWriter.WriteUnsignedIntegerField(ref writer, name, value, options);
    }

    private static void WriteDecimal(
        ref MessagePackWriter writer,
        string name,
        decimal value,
        MessagePackSerializerOptions options)
    {
        if (decimal.Truncate(value) == value &&
            value >= long.MinValue &&
            value <= long.MaxValue)
        {
            ProjectedPayloadWriter.WriteIntegerField(ref writer, name, (long)value, options);
            return;
        }

        ProjectedPayloadWriter.WriteNumberField(ref writer, name, (double)value, options);
    }

    private static bool IsSignedInteger(Type type) =>
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long);

    private static bool IsUnsignedInteger(Type type) => type == typeof(ulong);
    private static Expression ToLong(Expression expression) => Expression.Convert(expression, typeof(long));
    private static Expression ToUInt64(Expression expression) => Expression.Convert(expression, typeof(ulong));
    private static Expression ToDouble(Expression expression) => Expression.Convert(expression, typeof(double));
}

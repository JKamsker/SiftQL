using System.Linq.Expressions;
using System.Reflection;

namespace SiftQL.Schema;

internal static class FilterFieldAccessExpression
{
    public static Expression? Build(Expression subject, string propertyPath)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        Expression root = subject;
        Expression current = subject;
        var guards = new List<Expression>();
        foreach (string segment in propertyPath.Split('.'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                return null;
            if (!ReferenceEquals(current, root) && CanBeNull(current.Type))
                guards.Add(current);

            MemberInfo? member = FindMember(current.Type, segment);
            if (member is PropertyInfo { GetMethod: { } getter } property &&
                getter.GetParameters().Length == 0)
            {
                current = Expression.Property(current, property);
                continue;
            }

            if (member is FieldInfo field)
            {
                current = Expression.Field(current, field);
                continue;
            }

            return null;
        }

        return guards.Count == 0 ? current : Guard(current, guards);
    }

    private static Expression Guard(Expression value, IReadOnlyList<Expression> guards)
    {
        Type resultType = LiftValueType(value.Type);
        Expression result = resultType == value.Type
            ? value
            : Expression.Convert(value, resultType);

        for (int i = guards.Count - 1; i >= 0; i--)
        {
            result = Expression.Condition(
                IsNull(guards[i]),
                Expression.Default(resultType),
                result);
        }

        return result;
    }

    private static Type LiftValueType(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? typeof(Nullable<>).MakeGenericType(type)
            : type;

    private static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Expression IsNull(Expression expression)
    {
        if (Nullable.GetUnderlyingType(expression.Type) is not null)
            return Expression.Not(Expression.Property(expression, nameof(Nullable<int>.HasValue)));

        return Expression.Equal(expression, Expression.Constant(null, expression.Type));
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            MemberInfo? exact = FindDeclaredProperty(current, name, ignoreCase: false) ??
                (MemberInfo?)FindDeclaredField(current, name, ignoreCase: false);
            if (exact is not null)
                return exact;

            MemberInfo? ignoreCase = FindDeclaredProperty(current, name, ignoreCase: true) ??
                (MemberInfo?)FindDeclaredField(current, name, ignoreCase: true);
            if (ignoreCase is not null)
                return ignoreCase;
        }

        return null;
    }

    private static PropertyInfo? FindDeclaredProperty(Type type, string name, bool ignoreCase)
    {
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (PropertyInfo property in type.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (string.Equals(property.Name, name, comparison) &&
                property.GetMethod is not null &&
                property.GetMethod.GetParameters().Length == 0)
            {
                return property;
            }
        }

        return null;
    }

    private static FieldInfo? FindDeclaredField(Type type, string name, bool ignoreCase)
    {
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (FieldInfo field in type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (string.Equals(field.Name, name, comparison))
                return field;
        }

        return null;
    }
}

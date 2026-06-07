using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Projection;

internal static class ProjectionFieldArrayCompiler
{
    private const int MaxComposedFields = 4;

    private static readonly ConstructorInfo s_fieldConstructor =
        typeof(ProjectedEventField).GetConstructor(Type.EmptyTypes)!;
    private static readonly MemberInfo s_fieldName =
        typeof(ProjectedEventField).GetProperty(nameof(ProjectedEventField.Name))!;
    private static readonly MemberInfo s_fieldValue =
        typeof(ProjectedEventField).GetProperty(nameof(ProjectedEventField.Value))!;

    public static Func<object, ProjectedEventField[]>? TryCompile<TContext>(
        Type subjectType,
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        IReadOnlyList<FilterField> schemaFields)
    {
        if (fields.Count != schemaFields.Count)
            throw new ArgumentException("Projection field metadata count does not match.", nameof(schemaFields));
        if (fields.Count == 0)
            return static _ => [];
        if (fields.Count > MaxComposedFields)
            return null;

        var subject = Expression.Parameter(typeof(object), "subject");
        var typedSubject = Expression.Convert(subject, subjectType);
        var elements = new Expression[fields.Count];

        for (int i = 0; i < fields.Count; i++)
        {
            var value = BuildValueExpression(typedSubject, schemaFields[i]);
            if (value is null)
                return null;

            elements[i] = Expression.MemberInit(
                Expression.New(s_fieldConstructor),
                Expression.Bind(s_fieldName, Expression.Constant(fields[i].Name)),
                Expression.Bind(s_fieldValue, value));
        }

        return Expression.Lambda<Func<object, ProjectedEventField[]>>(
            Expression.NewArrayInit(typeof(ProjectedEventField), elements),
            subject).Compile();
    }

    private static Expression? BuildValueExpression(Expression subject, FilterField field)
    {
        if (field.Kind != FilterFieldKind.Scalar)
            return null;

        if (field.Access is null && field.ProjectionAccessor is not null)
            return BuildProjectionAccessorExpression(subject, field.ProjectionAccessor);
        if (field.Access is null)
            return null;

        if (field.Access.PropertyPath?.Contains('.') == true && field.ProjectionAccessor is not null)
            return BuildProjectionAccessorExpression(subject, field.ProjectionAccessor);

        var value = field.Access.PropertyPath is { } path
            ? FilterFieldAccessExpression.Build(subject, path)
            : BuildConstantExpression(field);
        return value is null ? null : ProjectionValueExpression.TryBuild(field.ValueType, value);
    }

    private static Expression BuildProjectionAccessorExpression(
        Expression subject,
        Func<object, ProjectedEventValue> accessor) =>
        Expression.Invoke(
            Expression.Constant(accessor),
            Expression.Convert(subject, typeof(object)));

    private static Expression BuildConstantExpression(FilterField field)
    {
        object? value = field.Access?.ConstantValue;
        Type expressionType = value?.GetType() ?? field.ValueType;
        return Expression.Constant(value, expressionType);
    }
}

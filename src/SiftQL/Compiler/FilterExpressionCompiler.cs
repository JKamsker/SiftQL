using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterExpressionCompiler
{
    private const int MaxDepth = 16;
    private const int MaxNodes = 128;
    private const int MaxValues = 128;

    private static readonly MethodInfo s_compare = typeof(FilterValues).GetMethod(
        nameof(FilterValues.Compare),
        [typeof(object), typeof(FilterValue), typeof(FilterOperator)])!;
    private static readonly MethodInfo s_in = typeof(FilterValues).GetMethod(
        nameof(FilterValues.In),
        [typeof(object), typeof(FilterValue[])])!;
    private static readonly MethodInfo s_contains = typeof(FilterValues).GetMethod(
        nameof(FilterValues.Contains),
        [typeof(object), typeof(FilterValue)])!;

    public static Func<object, bool>? TryCompile(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory) =>
        TryCompilePredicate(schema, expression, errorFactory)?.ObjectPredicate;

    public static KernelPredicate? TryCompilePredicate(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        int nodes = 0;
        var parameter = Expression.Parameter(schema.SubjectType, "subject");
        Expression? body = BuildNode(schema, parameter, expression, 0, ref nodes, errorFactory);
        if (body is null)
            return null;

        Type delegateType = typeof(Func<,>).MakeGenericType(schema.SubjectType, typeof(bool));
        Delegate typed = Expression.Lambda(delegateType, body, parameter).Compile();
        return KernelPredicate.FromTypedDelegate(schema.SubjectType, typed);
    }

    private static Expression? BuildNode(
        FilterSchema schema,
        Expression subject,
        FilterExpression expression,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (++nodes > MaxNodes)
            throw Error(errorFactory, $"Filter exceeds the {MaxNodes} node limit.");
        if (depth > MaxDepth)
            throw Error(errorFactory, $"Filter exceeds the {MaxDepth} level depth limit.");

        return expression.Kind switch
        {
            FilterExpressionKind.Any => Expression.Constant(true),
            FilterExpressionKind.And => BuildComposite(schema, subject, expression.Children, depth, ref nodes, errorFactory, and: true),
            FilterExpressionKind.Or => BuildComposite(schema, subject, expression.Children, depth, ref nodes, errorFactory, and: false),
            FilterExpressionKind.Not => BuildNot(schema, subject, expression.Children, depth, ref nodes, errorFactory),
            FilterExpressionKind.Compare => BuildCompare(schema, subject, expression, errorFactory),
            FilterExpressionKind.In => BuildIn(schema, subject, expression, errorFactory),
            FilterExpressionKind.Exists => BuildExists(schema, subject, expression, errorFactory),
            FilterExpressionKind.Contains => BuildContains(schema, subject, expression, errorFactory),
            _ => throw Error(errorFactory, $"Unknown filter node kind '{expression.Kind}'."),
        };
    }

    private static Expression? BuildComposite(
        FilterSchema schema,
        Expression subject,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory,
        bool and)
    {
        if (children.Length == 0)
            throw Error(errorFactory, "Composite filters must have at least one child.");

        FilterExpression[] ordered = and && children.Length > 1
            ? children.OrderBy(FilterExpressionCost.Estimate).ToArray()
            : children;
        Expression? body = null;
        for (int i = 0; i < ordered.Length; i++)
        {
            Expression? child = BuildNode(schema, subject, ordered[i], depth + 1, ref nodes, errorFactory);
            if (child is null)
                return null;
            body = body is null
                ? child
                : and ? Expression.AndAlso(body, child) : Expression.OrElse(body, child);
        }

        return body;
    }

    private static Expression? BuildNot(
        FilterSchema schema,
        Expression subject,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (children.Length != 1)
            throw Error(errorFactory, "Not filters must have exactly one child.");

        Expression? child = BuildNode(schema, subject, children[0], depth + 1, ref nodes, errorFactory);
        return child is null ? null : Expression.Not(child);
    }

    private static Expression? BuildCompare(
        FilterSchema schema,
        Expression subject,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        FilterValues.ValidateComparison(field, expression.Operator, value, errorFactory);

        Expression? access = BuildAccess(subject, field);
        return access is null
            ? null
            : FilterExpressionScalarBuilder.BuildCompare(access, value, expression.Operator) ??
                Expression.Call(
                    s_compare,
                    Expression.Convert(access, typeof(object)),
                    Expression.Constant(value),
                    Expression.Constant(expression.Operator));
    }

    private static Expression? BuildIn(
        FilterSchema schema,
        Expression subject,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        ValidateValues(field, expression.Values, errorFactory);

        Expression? access = BuildAccess(subject, field);
        return access is null
            ? null
            : FilterExpressionScalarBuilder.BuildIn(access, expression.Values) ??
                Expression.Call(
                    s_in,
                    Expression.Convert(access, typeof(object)),
                    Expression.Constant(expression.Values));
    }

    private static Expression? BuildExists(
        FilterSchema schema,
        Expression subject,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        Expression? access = BuildAccess(subject, field);
        return access is null ? null : Expression.Not(FilterExpressionNull.IsNull(access));
    }

    private static Expression? BuildContains(
        FilterSchema schema,
        Expression subject,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (field.Kind != FilterFieldKind.Array && field.ValueType != typeof(ProjectedEventValue))
            throw Error(errorFactory, $"Filter field '{field.Name}' is not a scalar array.");

        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        FilterValues.ValidateValue(field, value, errorFactory);
        if (value.Kind == FilterValueKind.Null)
            return null;

        Expression? access = BuildAccess(subject, field);
        return access is null
            ? null
            : FilterExpressionArrayBuilder.BuildContains(access, value) ??
                Expression.Call(
                    s_contains,
                    Expression.Convert(access, typeof(object)),
                    Expression.Constant(value));
    }

    private static Expression? BuildAccess(Expression subject, FilterField field)
    {
        if (field.Access?.PropertyPath is { } path)
        {
            Expression current = subject;
            foreach (string segment in path.Split('.'))
                current = Expression.PropertyOrField(current, segment);
            return current;
        }

        if (field.Access is not null)
            return Expression.Constant(field.Access.ConstantValue, field.Access.ConstantValue?.GetType() ?? field.ValueType);

        return Expression.Invoke(
            Expression.Constant(field.Getter),
            Expression.Convert(subject, typeof(object)));
    }

    private static FilterField RequireField(FilterSchema schema, string fieldName, Func<string, Exception>? errorFactory) =>
        schema.TryGetField(fieldName, out var field)
            ? field
            : throw Error(errorFactory, $"Filter field '{fieldName}' is not supported by {schema.SubjectType.FullName}.");

    private static void EnsureScalar(FilterField field, Func<string, Exception>? errorFactory)
    {
        if (field.Kind != FilterFieldKind.Scalar)
            throw Error(errorFactory, $"Filter field '{field.Name}' is not scalar.");
    }

    private static void ValidateValues(FilterField field, FilterValue[] values, Func<string, Exception>? errorFactory)
    {
        if (values.Length == 0 || values.Length > MaxValues)
            throw Error(errorFactory, $"Filter field '{field.Name}' must have between 1 and {MaxValues} values.");
        for (int i = 0; i < values.Length; i++)
            FilterValues.ValidateValue(field, values[i], errorFactory);
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

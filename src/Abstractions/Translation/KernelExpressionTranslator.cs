using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class KernelExpressionTranslator
{
    public static FilterExpression Translate<TSubject>(
        Expression<Func<TSubject, bool>> predicate)
    {
        int parameterIndex = 0;
        return Translate(predicate.Body, predicate.Parameters[0], ref parameterIndex);
    }

    private static FilterExpression Translate(Expression expression, ParameterExpression parameter, ref int parameterIndex)
    {
        expression = StripConvert(expression);
        return expression.NodeType switch
        {
            ExpressionType.AndAlso => TranslateBinary((BinaryExpression)expression, parameter, ref parameterIndex, FilterExpression.And),
            ExpressionType.OrElse => TranslateBinary((BinaryExpression)expression, parameter, ref parameterIndex, FilterExpression.Or),
            ExpressionType.Not => FilterExpression.Not(Translate(((UnaryExpression)expression).Operand, parameter, ref parameterIndex)),
            ExpressionType.Equal => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.Equal),
            ExpressionType.NotEqual => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.NotEqual),
            ExpressionType.GreaterThan => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.GreaterThan),
            ExpressionType.GreaterThanOrEqual => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.GreaterThanOrEqual),
            ExpressionType.LessThan => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.LessThan),
            ExpressionType.LessThanOrEqual => TranslateComparison((BinaryExpression)expression, parameter, ref parameterIndex, FilterOperator.LessThanOrEqual),
            ExpressionType.Call => TranslateMethodCall((MethodCallExpression)expression, parameter, ref parameterIndex),
            ExpressionType.MemberAccess => TranslateBooleanField((MemberExpression)expression, parameter),
            _ => throw Unsupported(expression),
        };
    }

    private static FilterExpression TranslateBinary(
        BinaryExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        Func<FilterExpression[], FilterExpression> combine) =>
        combine([Translate(expression.Left, parameter, ref parameterIndex), Translate(expression.Right, parameter, ref parameterIndex)]);

    private static FilterExpression TranslateComparison(
        BinaryExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        FilterOperator op)
    {
        if (TryGetFieldPath(expression.Left, parameter, out string? leftField))
            return FilterExpression.Compare(leftField, op, ToValue(expression.Right, parameter, ref parameterIndex));

        if (TryGetFieldPath(expression.Right, parameter, out string? rightField))
            return FilterExpression.Compare(rightField, Flip(op), ToValue(expression.Left, parameter, ref parameterIndex));

        throw Unsupported(expression);
    }

    private static FilterExpression TranslateMethodCall(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        if (IsKernelIn(expression.Method))
        {
            string field = RequireField(expression.Arguments[0], parameter);
            return FilterExpression.In(field, ToValues(expression.Arguments[1], parameter, ref parameterIndex));
        }

        if (IsKernelExists(expression.Method))
            return FilterExpression.Exists(RequireField(expression.Arguments[0], parameter));

        if (IsContains(expression.Method))
            return TranslateContains(expression, parameter, ref parameterIndex);

        throw Unsupported(expression);
    }

    private static FilterExpression TranslateContains(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        if (expression.Object is not null)
        {
            if (TryGetFieldPath(expression.Object, parameter, out string? field))
            {
                if (expression.Object.Type == typeof(string) && expression.Arguments.Count != 1)
                    throw Unsupported(expression);

                FilterValue value = ToValue(expression.Arguments[0], parameter, ref parameterIndex);
                return expression.Object.Type == typeof(string)
                    ? FilterExpression.StringContains(field, value)
                    : FilterExpression.Contains(field, value);
            }

            return FilterExpression.In(
                RequireField(expression.Arguments[0], parameter),
                ToValues(expression.Object, parameter, ref parameterIndex));
        }

        if (expression.Arguments.Count == 2)
        {
            if (TryGetFieldPath(expression.Arguments[0], parameter, out string? collectionField))
            {
                return FilterExpression.Contains(collectionField, ToValue(expression.Arguments[1], parameter, ref parameterIndex));
            }

            return FilterExpression.In(
                RequireField(expression.Arguments[1], parameter),
                ToValues(expression.Arguments[0], parameter, ref parameterIndex));
        }

        throw Unsupported(expression);
    }

    private static FilterExpression TranslateBooleanField(
        MemberExpression expression,
        ParameterExpression parameter)
    {
        string field = RequireField(expression, parameter);
        return FilterExpression.Compare(field, FilterOperator.Equal, FilterValue.From(true));
    }

    private static IReadOnlyCollection<FilterValue> ToValues(
        Expression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        object? value = KernelExpressionEvaluator.Evaluate(expression, parameter);
        if (value is string)
        {
            throw new KernelExpressionException("String constants are scalar values, not filter value lists.");
        }

        if (value is not IEnumerable enumerable)
        {
            throw new KernelExpressionException("Filter value list expression must evaluate to an enumerable.");
        }

        var values = new List<FilterValue>();
        foreach (object? item in enumerable)
        {
            values.Add(FilterValue.FromObject(item) with { ParameterKey = NextParameterKey(ref parameterIndex) });
        }

        return values;
    }

    private static FilterValue ToValue(
        Expression expression,
        ParameterExpression parameter,
        ref int parameterIndex) =>
        KernelExpressionEvaluator.EvaluateValue(
            expression,
            parameter,
            NextParameterKey(ref parameterIndex));

    private static string NextParameterKey(ref int parameterIndex) => "p" + parameterIndex++;

    private static string RequireField(Expression expression, ParameterExpression parameter) =>
        TryGetFieldPath(expression, parameter, out string? field)
            ? field
            : throw new KernelExpressionException($"Expression '{expression}' is not a filter field.");

    private static bool TryGetFieldPath(
        Expression expression,
        ParameterExpression parameter,
        out string field)
    {
        Expression original = expression;
        expression = StripConvert(expression);
        if (expression is MethodCallExpression implicitCall && IsImplicitConversion(implicitCall))
            return TryGetFieldPath(implicitCall.Arguments[0], parameter, out field);

        var names = new Stack<string>();
        Expression? current = expression;

        while (current is MemberExpression member)
        {
            if (member.Expression is null)
            {
                field = string.Empty;
                return false;
            }

            names.Push(member.Member.Name);
            current = StripConvert(member.Expression);
        }

        if (current is MethodCallExpression call &&
            TryGetProjectedFieldPath(call, parameter, out field))
        {
            if (!IsSupportedProjectedValueMember(names))
                throw Unsupported(original);

            return true;
        }

        if (current == parameter && names.Count > 0)
        {
            field = string.Join(".", names);
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static bool IsSupportedProjectedValueMember(Stack<string> names) =>
        names.Count == 0 || (names.Count == 1 &&
            names.Peek() is nameof(ProjectedEventValue.Boolean) or nameof(ProjectedEventValue.Integer) or
                nameof(ProjectedEventValue.UnsignedInteger) or nameof(ProjectedEventValue.Number) or
                nameof(ProjectedEventValue.Decimal) or nameof(ProjectedEventValue.String) or
                nameof(ProjectedEventValue.Guid));

    private static bool TryGetProjectedFieldPath(
        MethodCallExpression call,
        ParameterExpression parameter,
        out string field)
    {
        if (call.Method.DeclaringType != typeof(ProjectedEvent) ||
            call.Arguments.Count != 1 ||
            call.Object is null ||
            StripConvert(call.Object!) != parameter)
        {
            field = string.Empty;
            return false;
        }

        object? name = KernelExpressionEvaluator.Evaluate(
            StripConvert(call.Arguments[0]),
            parameter);
        if (name is not string text || string.IsNullOrWhiteSpace(text))
        {
            field = string.Empty;
            return false;
        }

        if (call.Method.Name == nameof(ProjectedEvent.Field))
        {
            field = ProjectedEventPaths.Field(text);
            return true;
        }

        if (call.Method.Name == nameof(ProjectedEvent.ContextValue))
        {
            field = ProjectedEventPaths.Context(text);
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression.NodeType is
            ExpressionType.Convert or
            ExpressionType.ConvertChecked or
            ExpressionType.TypeAs)
        {
            expression = ((UnaryExpression)expression).Operand;
        }

        return expression;
    }

    private static bool IsKernelIn(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.In));

    private static bool IsKernelExists(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.Exists));

    private static bool IsKernelPredicate(MethodInfo method, string name) =>
        method.Name == name &&
        method.DeclaringType == typeof(QueryKernelPredicates);

    private static bool IsContains(MethodInfo method) => method.Name is nameof(Enumerable.Contains) or "Contains";

    private static bool IsImplicitConversion(MethodCallExpression expression) =>
        expression.Method.Name == "op_Implicit" &&
        expression.Arguments.Count == 1;

    private static FilterOperator Flip(FilterOperator op) =>
        op switch
        {
            FilterOperator.GreaterThan => FilterOperator.LessThan,
            FilterOperator.GreaterThanOrEqual => FilterOperator.LessThanOrEqual,
            FilterOperator.LessThan => FilterOperator.GreaterThan,
            FilterOperator.LessThanOrEqual => FilterOperator.GreaterThanOrEqual,
            _ => op,
        };
    private static KernelExpressionException Unsupported(Expression expression) => new($"Unsupported server kernel expression '{expression}'.");
}

using System.Linq.Expressions;
using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using static SiftQL.Translation.ExpressionTranslationHelpers;

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
            return FilterExpression.Compare(
                leftField,
                op,
                KernelExpressionValues.ToValue(
                    expression.Right,
                    parameter,
                    ref parameterIndex,
                    ComparisonType(expression.Left)));

        if (TryGetFieldPath(expression.Right, parameter, out string? rightField))
            return FilterExpression.Compare(
                rightField,
                Flip(op),
                KernelExpressionValues.ToValue(
                    expression.Left,
                    parameter,
                    ref parameterIndex,
                    ComparisonType(expression.Right)));

        throw Unsupported(expression);
    }

    private static FilterExpression TranslateMethodCall(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        if (KernelElementAnyTranslator.TryTranslate(expression, parameter, ref parameterIndex, out var anyFilter))
            return anyFilter;

        if (IsKernelIn(expression.Method))
        {
            string field = RequireField(expression.Arguments[0], parameter);
            return FilterExpression.In(
                field,
                KernelExpressionValues.ToValues(expression.Arguments[1], parameter, ref parameterIndex));
        }

        if (IsKernelExists(expression.Method))
            return FilterExpression.Exists(RequireField(expression.Arguments[0], parameter));

        if (IsContains(expression.Method))
            return TranslateContains(expression, parameter, ref parameterIndex);

        if (IsStringMethod(expression.Method, nameof(string.Equals)))
            return TranslateStringEquals(expression, parameter, ref parameterIndex);

        if (IsStringMethod(expression.Method, nameof(string.StartsWith)))
            return TranslateStringMatch(expression, parameter, ref parameterIndex, FilterExpression.StringStartsWith);

        if (IsStringMethod(expression.Method, nameof(string.EndsWith)))
            return TranslateStringMatch(expression, parameter, ref parameterIndex, FilterExpression.StringEndsWith);

        if (IsStringMethod(expression.Method, nameof(string.IsNullOrEmpty)))
            return TranslateIsNullOrEmpty(expression, parameter);

        if (IsStringMethod(expression.Method, nameof(string.IsNullOrWhiteSpace)))
            throw new KernelExpressionException(
                $"'{expression}' is not supported: SiftQL has no whitespace operator. " +
                "Use string.IsNullOrEmpty for a null-or-empty check, or compare the field explicitly.");

        throw Unsupported(expression);
    }

    private static FilterExpression TranslateIsNullOrEmpty(
        MethodCallExpression expression,
        ParameterExpression parameter)
    {
        if (expression.Object is not null ||
            expression.Arguments.Count != 1 ||
            !TryGetFieldPath(expression.Arguments[0], parameter, out string? field))
        {
            throw Unsupported(expression);
        }

        return FilterExpression.Or(
            FilterExpression.Compare(field, FilterOperator.Equal, FilterValue.Null),
            FilterExpression.Compare(field, FilterOperator.Equal, FilterValue.From(string.Empty)));
    }

    private static FilterExpression TranslateStringMatch(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        Func<string, FilterValue, bool, FilterExpression> factory)
    {
        if (expression.Object is null ||
            expression.Object.Type != typeof(string) ||
            expression.Arguments.Count < 1 ||
            expression.Arguments[0].Type != typeof(string) ||
            !TryGetFieldPath(expression.Object, parameter, out string? field))
        {
            throw Unsupported(expression);
        }

        bool ignoreCase = expression.Arguments.Count switch
        {
            1 => false,
            2 when expression.Arguments[1].Type == typeof(StringComparison) =>
                ResolveIgnoreCase(expression.Arguments[1], parameter),
            _ => throw Unsupported(expression),
        };

        FilterValue value = KernelExpressionValues.ToValue(expression.Arguments[0], parameter, ref parameterIndex);
        return factory(field, value, ignoreCase);
    }

    private static FilterExpression TranslateStringEquals(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex)
    {
        Expression left;
        Expression right;
        Expression? comparison;
        if (expression.Object is not null)
        {
            left = expression.Object;
            (right, comparison) = expression.Arguments.Count switch
            {
                1 => (expression.Arguments[0], (Expression?)null),
                2 when expression.Arguments[1].Type == typeof(StringComparison) =>
                    (expression.Arguments[0], expression.Arguments[1]),
                _ => throw Unsupported(expression),
            };
        }
        else
        {
            (left, right, comparison) = expression.Arguments.Count switch
            {
                2 => (expression.Arguments[0], expression.Arguments[1], (Expression?)null),
                3 when expression.Arguments[2].Type == typeof(StringComparison) =>
                    (expression.Arguments[0], expression.Arguments[1], expression.Arguments[2]),
                _ => throw Unsupported(expression),
            };
        }

        bool ignoreCase = comparison is not null && ResolveIgnoreCase(comparison, parameter);

        if (TryGetFieldPath(left, parameter, out string? leftField))
            return FilterExpression.Compare(
                leftField,
                FilterOperator.Equal,
                KernelExpressionValues.ToValue(right, parameter, ref parameterIndex),
                ignoreCase);

        if (TryGetFieldPath(right, parameter, out string? rightField))
            return FilterExpression.Compare(
                rightField,
                FilterOperator.Equal,
                KernelExpressionValues.ToValue(left, parameter, ref parameterIndex),
                ignoreCase);

        throw Unsupported(expression);
    }

    private static bool ResolveIgnoreCase(Expression comparison, ParameterExpression parameter)
    {
        object? value = KernelExpressionEvaluator.Evaluate(StripConvert(comparison), parameter);
        return value switch
        {
            StringComparison.Ordinal => false,
            StringComparison.OrdinalIgnoreCase => true,
            _ => throw new KernelExpressionException(
                $"String comparison '{value}' is not supported in filters; use Ordinal or OrdinalIgnoreCase."),
        };
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
                if (expression.Object.Type == typeof(string))
                {
                    bool ignoreCase = expression.Arguments.Count switch
                    {
                        1 => false,
                        2 when expression.Arguments[1].Type == typeof(StringComparison) =>
                            ResolveIgnoreCase(expression.Arguments[1], parameter),
                        _ => throw Unsupported(expression),
                    };

                    FilterValue stringValue = KernelExpressionValues.ToValue(expression.Arguments[0], parameter, ref parameterIndex);
                    return FilterExpression.StringContains(field, stringValue, ignoreCase);
                }

                FilterValue value = KernelExpressionValues.ToValue(expression.Arguments[0], parameter, ref parameterIndex);
                return FilterExpression.Contains(field, value);
            }

            return FilterExpression.In(
                RequireField(expression.Arguments[0], parameter),
                KernelExpressionValues.ToValues(expression.Object, parameter, ref parameterIndex));
        }

        if (expression.Arguments.Count == 2)
        {
            if (TryGetFieldPath(expression.Arguments[0], parameter, out string? collectionField))
            {
                return FilterExpression.Contains(
                    collectionField,
                    KernelExpressionValues.ToValue(expression.Arguments[1], parameter, ref parameterIndex));
            }

            return FilterExpression.In(
                RequireField(expression.Arguments[1], parameter),
                KernelExpressionValues.ToValues(expression.Arguments[0], parameter, ref parameterIndex));
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

    private static string RequireField(Expression expression, ParameterExpression parameter) =>
        TryGetFieldPath(expression, parameter, out string? field)
            ? field
            : throw new KernelExpressionException($"Expression '{expression}' is not a filter field.");

    internal static bool TryGetFieldPath(
        Expression expression,
        ParameterExpression parameter,
        out string field)
    {
        Expression original = expression;
        ValidateFieldConversion(expression, parameter, Unsupported);
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

    internal static bool IsKernelIn(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.In));

    private static bool IsKernelExists(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.Exists));

    private static bool IsKernelPredicate(MethodInfo method, string name) =>
        method.Name == name &&
        method.DeclaringType == typeof(QueryKernelPredicates);

    internal static bool IsContains(MethodInfo method) => method.Name is nameof(Enumerable.Contains) or "Contains";

    private static bool IsStringMethod(MethodInfo method, string name) =>
        method.Name == name && method.DeclaringType == typeof(string);

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
    internal static KernelExpressionException Unsupported(Expression expression) =>
        new($"Unsupported server kernel expression '{expression}'. {Explain(expression)}");

    private static string Explain(Expression expression) =>
        expression.NodeType switch
        {
            ExpressionType.Add or ExpressionType.AddChecked or
            ExpressionType.Subtract or ExpressionType.SubtractChecked or
            ExpressionType.Multiply or ExpressionType.MultiplyChecked or
            ExpressionType.Divide or ExpressionType.Modulo or ExpressionType.Power =>
                "Arithmetic is not supported inside filter predicates; precompute the value or capture it as a variable so it becomes a constant.",
            ExpressionType.Conditional =>
                "Conditional (ternary) expressions are not supported; express each branch as a separate predicate combined with && or ||.",
            ExpressionType.Coalesce =>
                "Null-coalescing (??) is not supported; compare the field explicitly (e.g. field == null || field == value).",
            ExpressionType.Call when expression is MethodCallExpression call =>
                $"The method '{call.Method.Name}' cannot be translated. Supported methods: Contains, StartsWith, EndsWith, IsNullOrEmpty, In, Exists, and Any over a collection.",
            ExpressionType.Equal or ExpressionType.NotEqual or
            ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
            ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                when expression is BinaryExpression binary => ExplainComparison(binary),
            _ =>
                "Filter predicates support field comparisons (==, !=, <, <=, >, >=), &&, ||, !, Contains, StartsWith, EndsWith, IsNullOrEmpty, In, Exists, and Any.",
        };

    private static string ExplainComparison(BinaryExpression binary)
    {
        if (IsArithmetic(binary.Left) || IsArithmetic(binary.Right))
            return "Arithmetic is not supported inside filter predicates; precompute the value or capture it as a variable so it becomes a constant.";
        if (TryGetUnsupportedCall(binary.Left, out string? method) ||
            TryGetUnsupportedCall(binary.Right, out method))
        {
            return $"The method '{method}' cannot be translated; SiftQL compares raw field values. Supported string methods: Contains, StartsWith, EndsWith, IsNullOrEmpty.";
        }

        return "One side of the comparison must be a filter field (a property of the event parameter) and the other a constant or captured value.";
    }

    private static bool IsArithmetic(Expression expression) =>
        StripConvert(expression).NodeType is
            ExpressionType.Add or ExpressionType.AddChecked or
            ExpressionType.Subtract or ExpressionType.SubtractChecked or
            ExpressionType.Multiply or ExpressionType.MultiplyChecked or
            ExpressionType.Divide or ExpressionType.Modulo or ExpressionType.Power;

    private static bool TryGetUnsupportedCall(Expression expression, out string? method)
    {
        if (StripConvert(expression) is MethodCallExpression call)
        {
            method = call.Method.Name;
            return true;
        }

        method = null;
        return false;
    }
}

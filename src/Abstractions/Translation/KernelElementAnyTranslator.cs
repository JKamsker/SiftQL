using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Expressions;
using static SiftQL.Translation.ExpressionTranslationHelpers;

namespace SiftQL.Translation;

internal static class KernelElementAnyTranslator
{
    public static bool TryTranslate(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        out FilterExpression filter)
    {
        if (IsAll(expression.Method))
            return TryTranslateAll(expression, parameter, ref parameterIndex, out filter);

        return TryTranslateAny(expression, parameter, fieldPrefix: string.Empty, ref parameterIndex, out filter);
    }

    // All(predicate) == Not(Any(!predicate)). Sound for every element-predicate
    // shape Any supports after negation (boolean members in particular); other
    // shapes throw from the Any path, which is correct -- no silent mismatch.
    private static bool TryTranslateAll(
        MethodCallExpression expression,
        ParameterExpression parameter,
        ref int parameterIndex,
        out FilterExpression filter)
    {
        filter = FilterExpression.Any;
        if (expression.Arguments.Count != 2 ||
            Lambda(expression.Arguments[1]) is not { } predicate ||
            predicate.Parameters.Count != 1 ||
            !KernelExpressionTranslator.TryGetFieldPath(expression.Arguments[0], parameter, out string? sourceField))
        {
            throw KernelExpressionTranslator.Unsupported(expression);
        }

        string nextPrefix = CombinePath(string.Empty, sourceField);

        // All(p) == Not(Any(!p)). Build Any(!p) by negating at the expression level
        // only when the body is not already a Not; when it is (e.g. i => !i.Active or
        // i => !(i.Id == id)), translate its operand positively instead -- otherwise
        // the extra Expression.Not produces a double negation TranslateNot cannot
        // lower, and the whole All() throws even though it is decorrelatable.
        Expression body = StripConvert(predicate.Body);
        FilterExpression existsNegated = body.NodeType == ExpressionType.Not
            ? TranslatePredicate(((UnaryExpression)body).Operand, predicate.Parameters[0], nextPrefix, ref parameterIndex)
            : TranslatePredicate(Expression.Not(body), predicate.Parameters[0], nextPrefix, ref parameterIndex);
        filter = FilterExpression.Not(existsNegated);
        return true;
    }

    private static bool TryTranslateAny(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex,
        out FilterExpression filter)
    {
        filter = FilterExpression.Any;
        if (!IsAny(expression.Method))
            return false;

        if (expression.Arguments.Count != 2 ||
            Lambda(expression.Arguments[1]) is not { } predicate ||
            predicate.Parameters.Count != 1 ||
            !KernelExpressionTranslator.TryGetFieldPath(expression.Arguments[0], parameter, out string? sourceField))
        {
            throw KernelExpressionTranslator.Unsupported(expression);
        }

        Expression body = StripConvert(predicate.Body);
        // Element-local predicates that are not simple equality/contains shapes
        // need ElemMatch. Otherwise decorrelation either is impossible
        // (i.Power > 10) or would lose correlation across nested Any clauses.
        if (string.IsNullOrEmpty(fieldPrefix) &&
            ShouldUseElemMatch(body))
        {
            filter = FilterExpression.ElemMatch(
                sourceField,
                KernelExpressionTranslator.TranslateElement(body, predicate.Parameters[0]));
            return true;
        }

        string nextPrefix = CombinePath(fieldPrefix, sourceField);
        filter = TranslatePredicate(body, predicate.Parameters[0], nextPrefix, ref parameterIndex);
        return true;
    }

    private static bool ShouldUseElemMatch(Expression body) =>
        body.NodeType is
            ExpressionType.AndAlso or
            ExpressionType.NotEqual or
            ExpressionType.GreaterThan or
            ExpressionType.GreaterThanOrEqual or
            ExpressionType.LessThan or
            ExpressionType.LessThanOrEqual ||
        body is MethodCallExpression call && IsAny(call.Method);

    private static FilterExpression TranslatePredicate(
        Expression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        expression = StripConvert(expression);
        if (expression == parameter && IsScalar(parameter.Type))
            return FilterExpression.Contains(fieldPrefix, FilterValue.From(true));

        return expression.NodeType switch
        {
            ExpressionType.OrElse => TranslateOr((BinaryExpression)expression, parameter, fieldPrefix, ref parameterIndex),
            ExpressionType.Equal => TranslateEquality((BinaryExpression)expression, parameter, fieldPrefix, ref parameterIndex),
            ExpressionType.Call => TranslateCall((MethodCallExpression)expression, parameter, fieldPrefix, ref parameterIndex),
            ExpressionType.MemberAccess => TranslateBooleanField(expression, parameter, fieldPrefix, expected: true),
            ExpressionType.Not => TranslateNot((UnaryExpression)expression, parameter, fieldPrefix),
            _ => throw KernelExpressionTranslator.Unsupported(expression),
        };
    }
    private static FilterExpression TranslateOr(
        BinaryExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex) =>
        FilterExpression.Or(
            TranslatePredicate(expression.Left, parameter, fieldPrefix, ref parameterIndex),
            TranslatePredicate(expression.Right, parameter, fieldPrefix, ref parameterIndex));
    private static FilterExpression TranslateEquality(
        BinaryExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (TryGetElementFieldPath(expression.Left, parameter, fieldPrefix, out string? leftField))
        {
            return FilterExpression.Contains(
                leftField,
                KernelExpressionValues.ToValue(
                    expression.Right,
                    parameter,
                    ref parameterIndex,
                    ComparisonType(expression.Left)));
        }

        if (TryGetElementFieldPath(expression.Right, parameter, fieldPrefix, out string? rightField))
        {
            return FilterExpression.Contains(
                rightField,
                KernelExpressionValues.ToValue(
                    expression.Left,
                    parameter,
                    ref parameterIndex,
                    ComparisonType(expression.Right)));
        }

        throw KernelExpressionTranslator.Unsupported(expression);
    }
    private static FilterExpression TranslateCall(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (TryTranslateAny(expression, parameter, fieldPrefix, ref parameterIndex, out var nestedAny))
            return nestedAny;
        if (KernelExpressionTranslator.IsKernelIn(expression.Method))
            return TranslateKernelIn(expression, parameter, fieldPrefix, ref parameterIndex);
        if (KernelExpressionTranslator.IsContains(expression.Method))
            return TranslateContains(expression, parameter, fieldPrefix, ref parameterIndex);

        throw KernelExpressionTranslator.Unsupported(expression);
    }
    private static FilterExpression TranslateKernelIn(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (expression.Arguments.Count == 2 &&
            TryGetElementFieldPath(expression.Arguments[0], parameter, fieldPrefix, out string? field))
        {
            return ContainsAny(
                field,
                KernelExpressionValues.ToValues(expression.Arguments[1], parameter, ref parameterIndex));
        }

        throw KernelExpressionTranslator.Unsupported(expression);
    }
    private static FilterExpression TranslateContains(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (expression.Object is not null)
            return TranslateInstanceContains(expression, parameter, fieldPrefix, ref parameterIndex);

        if (expression.Arguments.Count == 2)
            return TranslateStaticContains(expression, parameter, fieldPrefix, ref parameterIndex);

        throw KernelExpressionTranslator.Unsupported(expression);
    }

    private static FilterExpression TranslateInstanceContains(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (TryGetElementFieldPath(expression.Object!, parameter, fieldPrefix, out string? collectionField))
        {
            if (expression.Object!.Type == typeof(string))
                throw KernelExpressionTranslator.Unsupported(expression);

            return FilterExpression.Contains(
                collectionField,
                KernelExpressionValues.ToValue(expression.Arguments[0], parameter, ref parameterIndex));
        }

        if (expression.Arguments.Count == 1 &&
            TryGetElementFieldPath(expression.Arguments[0], parameter, fieldPrefix, out string? valueField))
        {
            return ContainsAny(
                valueField,
                KernelExpressionValues.ToValues(expression.Object!, parameter, ref parameterIndex));
        }

        throw KernelExpressionTranslator.Unsupported(expression);
    }

    private static FilterExpression TranslateStaticContains(
        MethodCallExpression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        ref int parameterIndex)
    {
        if (TryGetElementFieldPath(expression.Arguments[0], parameter, fieldPrefix, out string? collectionField))
        {
            if (expression.Arguments[0].Type == typeof(string))
                throw KernelExpressionTranslator.Unsupported(expression);

            return FilterExpression.Contains(
                collectionField,
                KernelExpressionValues.ToValue(expression.Arguments[1], parameter, ref parameterIndex));
        }

        if (TryGetElementFieldPath(expression.Arguments[1], parameter, fieldPrefix, out string? valueField))
        {
            return ContainsAny(
                valueField,
                KernelExpressionValues.ToValues(expression.Arguments[0], parameter, ref parameterIndex));
        }

        throw KernelExpressionTranslator.Unsupported(expression);
    }

    private static FilterExpression TranslateBooleanField(
        Expression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        bool expected)
    {
        if (!TryGetElementFieldPath(expression, parameter, fieldPrefix, out string? field))
            throw KernelExpressionTranslator.Unsupported(expression);

        return FilterExpression.Contains(field, FilterValue.From(expected));
    }

    private static FilterExpression TranslateNot(
        UnaryExpression expression,
        ParameterExpression parameter,
        string fieldPrefix)
    {
        Expression operand = StripConvert(expression.Operand);
        if (operand == parameter && IsScalar(parameter.Type))
            return FilterExpression.Contains(fieldPrefix, FilterValue.From(false));

        return TranslateBooleanField(operand, parameter, fieldPrefix, expected: false);
    }

    private static bool TryGetElementFieldPath(
        Expression expression,
        ParameterExpression parameter,
        string fieldPrefix,
        out string field)
    {
        expression = StripConvert(expression);
        if (expression == parameter && IsScalar(parameter.Type))
        {
            field = fieldPrefix;
            return true;
        }

        if (KernelExpressionTranslator.TryGetFieldPath(expression, parameter, out string? relativeField))
        {
            field = CombinePath(fieldPrefix, relativeField);
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static FilterExpression ContainsAny(
        string field,
        IReadOnlyCollection<FilterValue> values)
    {
        if (values.Count == 0)
            return FilterExpression.Not(FilterExpression.Any);

        var filters = new FilterExpression[values.Count];
        int index = 0;
        foreach (FilterValue value in values)
            filters[index++] = FilterExpression.Contains(field, value);

        return FilterExpression.Or(filters);
    }

    private static string CombinePath(string prefix, string field) =>
        string.IsNullOrEmpty(prefix) ? field : prefix + "." + field;

    private static LambdaExpression? Lambda(Expression expression)
    {
        expression = StripConvert(expression);
        if (expression.NodeType == ExpressionType.Quote)
            return ((UnaryExpression)expression).Operand as LambdaExpression;

        return expression as LambdaExpression;
    }

    private static bool IsAny(MethodInfo method) =>
        method.Name == nameof(Enumerable.Any) &&
        method.DeclaringType is { } declaringType &&
        (declaringType == typeof(Enumerable) || declaringType == typeof(Queryable));

    private static bool IsAll(MethodInfo method) =>
        method.Name == nameof(Enumerable.All) &&
        method.DeclaringType is { } declaringType &&
        (declaringType == typeof(Enumerable) || declaringType == typeof(Queryable));

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum ||
            type == typeof(bool) ||
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
            type == typeof(decimal) ||
            type == typeof(string) ||
            type == typeof(Guid);
    }
}

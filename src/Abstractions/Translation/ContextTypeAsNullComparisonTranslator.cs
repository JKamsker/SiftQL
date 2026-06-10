using System.Linq.Expressions;
using SiftQL.Expressions;

namespace SiftQL.Translation;

internal static class ContextTypeAsNullComparisonTranslator
{
    public static bool TryTranslate(
        BinaryExpression expression,
        FilterOperator op,
        Func<Expression, string> resolveTypeTestField,
        Func<Expression, Type?, FilterValue> toValue,
        out FilterExpression? filter)
    {
        filter = null;
        if (op is not (FilterOperator.Equal or FilterOperator.NotEqual))
            return false;

        if (!TryGetTypeAsNullTest(expression.Left, expression.Right, out Type? targetType, out Expression? operand) &&
            !TryGetTypeAsNullTest(expression.Right, expression.Left, out targetType, out operand))
        {
            return false;
        }

        string typeName = targetType.FullName ??
            throw new KernelExpressionException(
                $"'as {targetType.Name}' null checks are not supported: the type has no metadata full name.");
        FilterExpression typeTest = FilterExpression.Contains(
            resolveTypeTestField(operand),
            toValue(Expression.Constant(typeName, typeof(string)), null));
        filter = op == FilterOperator.NotEqual ? typeTest : FilterExpression.Not(typeTest);
        return true;
    }

    private static bool TryGetTypeAsNullTest(
        Expression candidate,
        Expression other,
        out Type targetType,
        out Expression operand)
    {
        targetType = null!;
        operand = null!;
        if (StripConvert(other) is not ConstantExpression { Value: null })
            return false;

        candidate = StripConvertExceptTypeAs(candidate);
        if (candidate is not UnaryExpression { NodeType: ExpressionType.TypeAs } cast)
            return false;

        targetType = cast.Type;
        operand = cast.Operand;
        return true;
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

    private static Expression StripConvertExceptTypeAs(Expression expression)
    {
        while (expression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            expression = ((UnaryExpression)expression).Operand;
        return expression;
    }
}

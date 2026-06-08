using System.Linq.Expressions;

namespace SiftQL.Translation;

internal static class ExpressionTranslationHelpers
{
    public static Expression StripConvert(Expression expression)
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

    public static Type? ComparisonType(Expression expression)
    {
        while (expression.NodeType is
            ExpressionType.Convert or
            ExpressionType.ConvertChecked or
            ExpressionType.TypeAs)
        {
            Expression operand = ((UnaryExpression)expression).Operand;
            Type operandType = Nullable.GetUnderlyingType(operand.Type) ?? operand.Type;
            if (operandType.IsEnum)
                return operandType;
            expression = operand;
        }

        Type type = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
        return type.IsEnum ? type : null;
    }

    public static object? CoerceValue(object? value, Type? targetType)
    {
        if (value is null || targetType is null)
            return value;

        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!type.IsEnum || value.GetType().IsEnum)
            return value;

        return Enum.ToObject(type, value);
    }
}

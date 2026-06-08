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

    public static void ValidateFieldConversion(
        Expression expression,
        ParameterExpression parameter,
        Func<Expression, Exception> unsupported)
    {
        Expression current = expression;
        while (current.NodeType is
            ExpressionType.Convert or
            ExpressionType.ConvertChecked or
            ExpressionType.TypeAs)
        {
            var unary = (UnaryExpression)current;
            if (ReferencesParameter(unary.Operand, parameter) &&
                !CanStripFieldConversion(unary.Operand.Type, unary.Type))
            {
                throw unsupported(expression);
            }

            current = unary.Operand;
        }
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

    public static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        bool found = false;
        new ParameterReferenceVisitor(parameter, () => found = true).Visit(expression);
        return found;
    }

    private static bool CanStripFieldConversion(Type operandType, Type targetType)
    {
        Type source = Nullable.GetUnderlyingType(operandType) ?? operandType;
        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (source == target ||
            target == typeof(object) ||
            target.IsAssignableFrom(source))
        {
            return true;
        }

        if (source.IsEnum)
            return target == Enum.GetUnderlyingType(source);
        if (target.IsEnum)
            return false;

        return IsExactNumericWidening(source, target);
    }

    private static bool IsExactNumericWidening(Type source, Type target) =>
        Type.GetTypeCode(source) switch
        {
            TypeCode.SByte => target == typeof(short) || target == typeof(int) || target == typeof(long),
            TypeCode.Byte => target == typeof(short) || target == typeof(ushort) ||
                target == typeof(int) || target == typeof(uint) ||
                target == typeof(long) || target == typeof(ulong),
            TypeCode.Int16 => target == typeof(int) || target == typeof(long),
            TypeCode.UInt16 => target == typeof(int) || target == typeof(uint) ||
                target == typeof(long) || target == typeof(ulong),
            TypeCode.Int32 => target == typeof(long),
            TypeCode.UInt32 => target == typeof(long) || target == typeof(ulong),
            TypeCode.Char => target == typeof(ushort) || target == typeof(int) ||
                target == typeof(uint) || target == typeof(long) || target == typeof(ulong),
            _ => false,
        };

    private sealed class ParameterReferenceVisitor(
        ParameterExpression parameter,
        Action onFound) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter)
                onFound();

            return node;
        }
    }
}

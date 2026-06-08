using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class KernelExpressionEvaluator
{
    public static FilterValue EvaluateValue(
        Expression expression,
        ParameterExpression parameter,
        string parameterKey) =>
        FilterValue.FromObject(Evaluate(expression, parameter)) with { ParameterKey = parameterKey };

    public static object? Evaluate(Expression expression, ParameterExpression parameter)
    {
        if (ReferencesParameter(expression, parameter))
        {
            throw new KernelExpressionException(
                $"Expression '{expression}' is not a constant filter value.");
        }

        return TryEvaluateConstant(expression, out object? value)
            ? value
            : throw new KernelExpressionException(
                $"Expression '{expression}' is not a supported constant filter value.");
    }

    private static bool TryEvaluateConstant(Expression expression, out object? value)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case MemberExpression member:
                return TryEvaluateMember(member, out value);
            case UnaryExpression unary when IsConversion(unary.NodeType):
                return TryEvaluateConversion(unary, out value);
            case MethodCallExpression call when IsImplicitConversion(call):
                return TryEvaluateConstant(call.Arguments[0], out value);
            case NewArrayExpression array:
                return TryEvaluateArray(array, out value);
            default:
                value = null;
                return false;
        }
    }

    private static bool TryEvaluateMember(MemberExpression member, out object? value)
    {
        object? instance = null;
        if (member.Expression is not null &&
            !TryEvaluateConstant(member.Expression, out instance))
        {
            value = null;
            return false;
        }

        if (member.Member is FieldInfo field)
        {
            value = field.GetValue(instance);
            return true;
        }

        if (member.Member is PropertyInfo property &&
            property.GetMethod?.GetParameters().Length == 0)
        {
            value = property.GetValue(instance);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryEvaluateConversion(UnaryExpression expression, out object? value)
    {
        if (!TryEvaluateConstant(expression.Operand, out object? operand))
        {
            value = null;
            return false;
        }

        try
        {
            Expression converted = expression.Update(Expression.Constant(operand, expression.Operand.Type));
            value = Expression.Lambda<Func<object?>>(
                    Expression.Convert(converted, typeof(object)))
                .Compile()
                .Invoke();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    private static bool TryEvaluateArray(NewArrayExpression array, out object? value)
    {
        Type elementType = array.Type.GetElementType() ?? typeof(object);
        var values = Array.CreateInstance(elementType, array.Expressions.Count);
        for (int i = 0; i < array.Expressions.Count; i++)
        {
            if (!TryEvaluateConstant(array.Expressions[i], out object? item))
            {
                value = null;
                return false;
            }

            values.SetValue(item, i);
        }

        value = values;
        return true;
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        bool found = false;
        new ParameterVisitor(parameter, () => found = true).Visit(expression);
        return found;
    }

    private static bool IsConversion(ExpressionType nodeType) =>
        nodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs;

    private static bool IsImplicitConversion(MethodCallExpression expression) =>
        expression.Method.Name == "op_Implicit" &&
        expression.Arguments.Count == 1;

    private sealed class ParameterVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private readonly Action _onFound;

        public ParameterVisitor(ParameterExpression parameter, Action onFound)
        {
            _parameter = parameter;
            _onFound = onFound;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
            {
                _onFound();
            }

            return node;
        }
    }
}

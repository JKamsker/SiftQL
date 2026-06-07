using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class ProjectionSelectorConstantEvaluator
{
    public static FilterValue EvaluateValue(
        Expression expression,
        ParameterExpression subject,
        ParameterExpression context,
        string parameterKey) =>
        FilterValue.FromObject(Evaluate(expression, subject, context)) with { ParameterKey = parameterKey };

    public static object? Evaluate(
        Expression expression,
        ParameterExpression subject,
        ParameterExpression context)
    {
        expression = StripConvert(expression);
        if (ReferencesParameter(expression, subject) || ReferencesParameter(expression, context))
        {
            throw new KernelExpressionException(
                $"Projection argument '{expression}' is not a constant value.");
        }

        return TryEvaluateConstant(expression, out object? value)
            ? value
            : throw new KernelExpressionException(
                $"Projection argument '{expression}' is not a supported constant value.");
    }

    private static bool TryEvaluateConstant(Expression expression, out object? value)
    {
        expression = StripConvert(expression);
        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        if (expression is MemberExpression member)
            return TryEvaluateMember(member, out value);

        value = null;
        return false;
    }

    private static bool TryEvaluateMember(MemberExpression member, out object? value)
    {
        object? instance = null;
        if (member.Expression is not null && !TryEvaluateConstant(member.Expression, out instance))
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
            property.GetMethod is { IsStatic: false } &&
            property.GetMethod.GetParameters().Length == 0)
        {
            value = property.GetValue(instance);
            return true;
        }

        value = null;
        return false;
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        bool found = false;
        new ParameterReferenceVisitor(parameter, () => found = true).Visit(expression);
        return found;
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

    private sealed class ParameterReferenceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private readonly Action _onFound;

        public ParameterReferenceVisitor(ParameterExpression parameter, Action onFound)
        {
            _parameter = parameter;
            _onFound = onFound;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
                _onFound();
            return node;
        }
    }
}

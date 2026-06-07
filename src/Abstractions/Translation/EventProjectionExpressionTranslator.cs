using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class EventProjectionExpressionTranslator
{
    public static EventProjectionField Translate<TSubject>(
        Expression<Func<TSubject, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        string path = FieldPath(StripConvert(selector.Body), selector.Parameters[0]);
        return new EventProjectionField(path);
    }

    private static string FieldPath(Expression expression, ParameterExpression parameter)
    {
        var parts = new Stack<string>();
        Expression? current = expression;

        while (current is not null)
        {
            current = StripConvert(current);
            if (current == parameter)
                return string.Join(".", parts);

            if (current is not MemberExpression member)
                break;

            parts.Push(member.Member.Name);
            current = member.Expression;
        }

        throw new KernelExpressionException(
            $"Projection expression '{expression}' is not a field selector.");
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
               } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }
}

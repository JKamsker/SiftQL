using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class ProjectedSelectorTranslator
{
    public static EventProjectionExpression Translate<TProjection, TNext>(
        Expression<Func<TProjection, TNext>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var fields = new List<EventProjectionField>();
        TranslateValue(StripConvert(selector.Body), selector.Parameters[0], name: null, fields);
        if (fields.Count == 0)
            throw new KernelExpressionException("Projection selector must include at least one field.");

        return EventProjectionExpression.Default.WithFields(fields);
    }

    private static void TranslateValue(
        Expression expression,
        ParameterExpression subject,
        string? name,
        List<EventProjectionField> fields)
    {
        expression = StripConvert(expression);
        if (expression is NewExpression created && created.Members is not null)
        {
            for (int i = 0; i < created.Arguments.Count; i++)
                TranslateValue(created.Arguments[i], subject, created.Members[i].Name, fields);
            return;
        }

        if (expression is MemberInitExpression initialized)
        {
            for (int i = 0; i < initialized.Bindings.Count; i++)
            {
                if (initialized.Bindings[i] is not MemberAssignment assignment)
                    throw Unsupported(initialized.Bindings[i]);
                TranslateValue(assignment.Expression, subject, assignment.Member.Name, fields);
            }

            return;
        }

        if (TryGetFieldPath(expression, subject, out string? fieldPath))
        {
            fields.Add(new EventProjectionField(
                ProjectedEventPaths.Field(fieldPath),
                name ?? fieldPath));
            return;
        }

        throw Unsupported(expression);
    }

    private static bool TryGetFieldPath(
        Expression expression,
        ParameterExpression subject,
        out string field)
    {
        expression = StripConvert(expression);
        var names = new Stack<string>();
        Expression? current = expression;
        while (current is MemberExpression member)
        {
            names.Push(member.Member.Name);
            current = StripConvert(member.Expression!);
        }

        if (current == subject && names.Count > 0)
        {
            field = string.Join(".", names);
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

    private static KernelExpressionException Unsupported(object expression) =>
        new($"Unsupported projection selector expression '{expression}'.");
}

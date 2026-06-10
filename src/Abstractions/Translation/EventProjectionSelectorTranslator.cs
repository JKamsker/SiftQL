using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal static class EventProjectionSelectorTranslator
{
    public static EventProjectionExpression Translate<TSubject>(
        Expression<Func<TSubject, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Translate(
            selector.Body,
            selector.Parameters[0]);
    }

    public static EventProjectionExpression Translate<TSubject>(
        Expression<Func<TSubject, ProjectionContext<TSubject>, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Translate(
            selector.Body,
            selector.Parameters[0]);
    }

    private static EventProjectionExpression Translate(
        Expression body,
        ParameterExpression subject)
    {
        var fields = new List<EventProjectionField>();
        var constants = new ProjectionSelectorConstantTranslator(subject);
        TranslateValue(StripConvert(body), subject, name: null, fields, constants);
        if (fields.Count == 0 && constants.Includes.Length == 0)
        {
            throw new KernelExpressionException(
                "Projection selector must include at least one field or local value.");
        }

        return EventProjectionExpression.Default
            .WithFields(fields)
            .WithIncludes(constants.Includes);
    }

    private static void TranslateValue(
        Expression expression,
        ParameterExpression subject,
        string? name,
        List<EventProjectionField> fields,
        ProjectionSelectorConstantTranslator constants)
    {
        expression = StripConvert(expression);
        if (expression is NewExpression created && created.Members is not null)
        {
            for (int i = 0; i < created.Arguments.Count; i++)
                TranslateValue(created.Arguments[i], subject, created.Members[i].Name, fields, constants);
            return;
        }

        if (expression is MemberInitExpression initialized)
        {
            TranslateMemberInit(initialized, subject, fields, constants);
            return;
        }

        if (TryGetFieldPath(expression, subject, out string? fieldPath))
        {
            fields.Add(new EventProjectionField(fieldPath, name));
            return;
        }

        constants.Translate(expression, name);
    }

    private static void TranslateMemberInit(
        MemberInitExpression initialized,
        ParameterExpression subject,
        List<EventProjectionField> fields,
        ProjectionSelectorConstantTranslator constants)
    {
        for (int i = 0; i < initialized.Bindings.Count; i++)
        {
            if (initialized.Bindings[i] is not MemberAssignment assignment)
                throw Unsupported(initialized.Bindings[i]);
            TranslateValue(assignment.Expression, subject, assignment.Member.Name, fields, constants);
        }
    }

    private static bool TryGetFieldPath(
        Expression expression,
        ParameterExpression subject,
        out string field) =>
        KernelExpressionTranslator.TryGetFieldPath(expression, subject, out field);

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

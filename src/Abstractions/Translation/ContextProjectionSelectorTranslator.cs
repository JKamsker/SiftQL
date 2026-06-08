using System.Linq.Expressions;
using SiftQL.Expressions;

namespace SiftQL.Translation;

internal static class ContextProjectionSelectorTranslator
{
    public static ContextSelectorTranslation Translate<TSubject, TContext, TProjection>(
        Expression<Func<TSubject, TContext, TProjection>> selector,
        IReadOnlyList<ContextProjectionBinding> bindings,
        int parameterOffset)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var translator = new Translator(selector.Parameters[0], selector.Parameters[1], bindings, parameterOffset);
        translator.TranslateValue(StripConvert(selector.Body), name: null);
        if (translator.Outputs.Count == 0)
            throw new KernelExpressionException("Projection selector must include at least one field.");

        return new ContextSelectorTranslation(
            translator.Outputs.ToArray(),
            [.. translator.Includes.NewIncludes, .. translator.Constants.Includes],
            translator.Includes.Bindings);
    }

    private sealed class Translator
    {
        private readonly ParameterExpression _subject;
        private int _parameterIndex;

        public Translator(
            ParameterExpression subject,
            ParameterExpression context,
            IReadOnlyList<ContextProjectionBinding> bindings,
            int parameterOffset)
        {
            _subject = subject;
            _parameterIndex = parameterOffset;
            Includes = new ContextExpressionIncludes(subject, context, bindings, NextParameterKey);
            Constants = new ProjectionSelectorConstantTranslator(subject, context, NextParameterKey);
        }

        public List<ContextSelectorOutput> Outputs { get; } = [];
        public ContextExpressionIncludes Includes { get; }
        public ProjectionSelectorConstantTranslator Constants { get; }

        public void TranslateValue(Expression expression, string? name)
        {
            expression = StripConvert(expression);
            if (expression is NewExpression created && created.Members is not null)
            {
                for (int i = 0; i < created.Arguments.Count; i++)
                    TranslateValue(created.Arguments[i], created.Members[i].Name);
                return;
            }

            if (expression is MemberInitExpression initialized)
            {
                TranslateMemberInit(initialized);
                return;
            }

            if (TryGetSubjectFieldPath(expression, out string? fieldPath))
            {
                Outputs.Add(ContextSelectorOutput.SourceField(fieldPath, name ?? fieldPath));
                return;
            }

            if (Includes.TryTranslate(expression, name, out string contextPath))
            {
                Outputs.Add(ContextSelectorOutput.ContextField(contextPath, RequiredName(name, expression)));
                return;
            }

            Outputs.Add(ContextSelectorOutput.ContextField(
                Constants.Translate(expression, name),
                RequiredName(name, expression)));
        }

        private void TranslateMemberInit(MemberInitExpression initialized)
        {
            for (int i = 0; i < initialized.Bindings.Count; i++)
            {
                if (initialized.Bindings[i] is not MemberAssignment assignment)
                    throw new KernelExpressionException(
                        $"Unsupported projection selector expression '{initialized.Bindings[i]}'.");
                TranslateValue(assignment.Expression, assignment.Member.Name);
            }
        }

        private bool TryGetSubjectFieldPath(Expression expression, out string fieldPath)
        {
            expression = StripConvert(expression);
            var names = new Stack<string>();
            Expression? current = expression;
            while (current is MemberExpression member)
            {
                names.Push(member.Member.Name);
                current = StripConvert(member.Expression!);
            }

            if (current == _subject && names.Count > 0)
            {
                fieldPath = string.Join(".", names);
                return true;
            }

            fieldPath = string.Empty;
            return false;
        }

        private static string RequiredName(string? name, Expression expression) =>
            string.IsNullOrWhiteSpace(name)
                ? throw new KernelExpressionException(
                    $"Projection selector expression '{expression}' requires a result name.")
                : name;

        private string NextParameterKey() =>
            "p" + _parameterIndex++;
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
}

internal sealed record ContextSelectorTranslation(
    ContextSelectorOutput[] Outputs,
    EventProjectionInclude[] NewIncludes,
    ContextProjectionBinding[] Bindings);

internal sealed record ContextSelectorOutput(
    string SourcePath,
    string ProjectedPath,
    string Name,
    bool IsContext)
{
    public static ContextSelectorOutput SourceField(string sourcePath, string name) =>
        new(sourcePath, string.Empty, name, IsContext: false);

    public static ContextSelectorOutput ContextField(string projectedPath, string name) =>
        new(string.Empty, projectedPath, name, IsContext: true);
}

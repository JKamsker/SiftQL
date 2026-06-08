using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal sealed class ProjectionSelectorConstantTranslator
{
    private readonly ParameterExpression[] _parameters;
    private readonly Func<string> _nextParameterKey;
    private readonly List<EventProjectionInclude> _includes = [];
    private int _parameterIndex;

    public ProjectionSelectorConstantTranslator(ParameterExpression subject)
    {
        _parameters = [subject];
        _nextParameterKey = NextLocalParameterKey;
    }

    public ProjectionSelectorConstantTranslator(
        ParameterExpression subject,
        ParameterExpression context,
        Func<string> nextParameterKey)
    {
        _parameters = [subject, context];
        _nextParameterKey = nextParameterKey ?? throw new ArgumentNullException(nameof(nextParameterKey));
    }

    public EventProjectionInclude[] Includes => _includes.ToArray();

    public string Translate(Expression expression, string? name)
    {
        string resultName = RequiredName(name, expression);
        FilterValue value = _parameters.Length == 1
            ? KernelExpressionEvaluator.EvaluateValue(expression, _parameters[0], _nextParameterKey())
            : KernelExpressionEvaluator.EvaluateValue(
                expression,
                _parameters[0],
                _parameters[1],
                _nextParameterKey());
        var include = new EventProjectionInclude(
            EventProjectionConstantIntrinsics.Value,
            resultName,
            new EventProjectionArgument(EventProjectionConstantIntrinsics.ArgumentName, value));
        _includes.Add(include);
        return ProjectedEventPaths.Context(resultName);
    }

    private string NextLocalParameterKey() =>
        "p" + _parameterIndex++;

    private static string RequiredName(string? name, Expression expression) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new KernelExpressionException(
                $"Projection selector expression '{expression}' requires a result name.")
            : name;
}

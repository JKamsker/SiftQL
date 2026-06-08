using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Expressions;
using static SiftQL.Translation.ExpressionTranslationHelpers;

namespace SiftQL.Translation;

internal static class ContextKernelExpressionTranslator
{
    public static ContextFilterTranslation Translate<TSubject, TContext>(
        Expression<Func<TSubject, TContext, bool>> predicate,
        EventPipelineExpression pipeline,
        IReadOnlyList<ContextProjectionBinding> bindings,
        int parameterOffset,
        bool projectSubjectFields = true)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var translator = new Translator(
            predicate.Parameters[0],
            predicate.Parameters[1],
            pipeline,
            bindings,
            parameterOffset,
            projectSubjectFields);
        FilterExpression filter = translator.Translate(StripConvert(predicate.Body));
        return new ContextFilterTranslation(
            filter,
            translator.Includes.NewIncludes,
            translator.Includes.Bindings);
    }

    private sealed class Translator
    {
        private readonly ParameterExpression _subject;
        private readonly ParameterExpression _context;
        private readonly EventPipelineExpression _pipeline;
        private readonly bool _projectSubjectFields;
        private int _parameterIndex;

        public Translator(
            ParameterExpression subject,
            ParameterExpression context,
            EventPipelineExpression pipeline,
            IReadOnlyList<ContextProjectionBinding> bindings,
            int parameterOffset,
            bool projectSubjectFields)
        {
            _subject = subject;
            _context = context;
            _pipeline = pipeline;
            _projectSubjectFields = projectSubjectFields;
            _parameterIndex = parameterOffset;
            Includes = new ContextExpressionIncludes(subject, context, bindings, NextParameterKey);
        }

        public ContextExpressionIncludes Includes { get; }

        public FilterExpression Translate(Expression expression)
        {
            expression = StripConvert(expression);
            return expression.NodeType switch
            {
                ExpressionType.AndAlso => TranslateBinary((BinaryExpression)expression, FilterExpression.And),
                ExpressionType.OrElse => TranslateBinary((BinaryExpression)expression, FilterExpression.Or),
                ExpressionType.Not => FilterExpression.Not(Translate(((UnaryExpression)expression).Operand)),
                ExpressionType.Equal => TranslateComparison((BinaryExpression)expression, FilterOperator.Equal),
                ExpressionType.NotEqual => TranslateComparison((BinaryExpression)expression, FilterOperator.NotEqual),
                ExpressionType.GreaterThan => TranslateComparison((BinaryExpression)expression, FilterOperator.GreaterThan),
                ExpressionType.GreaterThanOrEqual => TranslateComparison((BinaryExpression)expression, FilterOperator.GreaterThanOrEqual),
                ExpressionType.LessThan => TranslateComparison((BinaryExpression)expression, FilterOperator.LessThan),
                ExpressionType.LessThanOrEqual => TranslateComparison((BinaryExpression)expression, FilterOperator.LessThanOrEqual),
                ExpressionType.Call => TranslateMethodCall((MethodCallExpression)expression),
                ExpressionType.MemberAccess => TranslateBooleanField(expression),
                _ => throw Unsupported(expression),
            };
        }

        private FilterExpression TranslateBinary(
            BinaryExpression expression,
            Func<FilterExpression[], FilterExpression> combine) =>
            combine([Translate(expression.Left), Translate(expression.Right)]);

        private FilterExpression TranslateComparison(BinaryExpression expression, FilterOperator op)
        {
            if (TryGetProjectedPath(expression.Left, out string? leftField))
                return FilterExpression.Compare(leftField, op, ToValue(expression.Right, ComparisonType(expression.Left)));

            if (TryGetProjectedPath(expression.Right, out string? rightField))
                return FilterExpression.Compare(rightField, Flip(op), ToValue(expression.Left, ComparisonType(expression.Right)));

            throw Unsupported(expression);
        }

        private FilterExpression TranslateMethodCall(MethodCallExpression expression)
        {
            if (IsKernelIn(expression.Method))
            {
                string field = RequireProjectedPath(expression.Arguments[0]);
                return FilterExpression.In(field, ToValues(expression.Arguments[1]));
            }

            if (IsKernelExists(expression.Method))
                return FilterExpression.Exists(RequireProjectedPath(expression.Arguments[0]));

            if (IsContains(expression.Method))
                return TranslateContains(expression);

            if (Includes.TryTranslate(expression, name: null, out string contextPath))
                return FilterExpression.Compare(contextPath, FilterOperator.Equal, FilterValue.From(true));

            throw Unsupported(expression);
        }

        private FilterExpression TranslateContains(MethodCallExpression expression)
        {
            if (expression.Object is not null)
            {
                if (TryGetProjectedPath(expression.Object, out string? field))
                {
                    if (expression.Object.Type == typeof(string) && expression.Arguments.Count != 1)
                        throw Unsupported(expression);

                    FilterValue value = ToValue(expression.Arguments[0]);
                    return expression.Object.Type == typeof(string)
                        ? FilterExpression.StringContains(field, value)
                        : FilterExpression.Contains(field, value);
                }

                return FilterExpression.In(RequireProjectedPath(expression.Arguments[0]), ToValues(expression.Object));
            }

            if (expression.Arguments.Count == 2)
            {
                if (TryGetProjectedPath(expression.Arguments[0], out string? collectionField))
                    return FilterExpression.Contains(collectionField, ToValue(expression.Arguments[1]));

                return FilterExpression.In(RequireProjectedPath(expression.Arguments[1]), ToValues(expression.Arguments[0]));
            }

            throw Unsupported(expression);
        }

        private FilterExpression TranslateBooleanField(Expression expression) =>
            FilterExpression.Compare(RequireProjectedPath(expression), FilterOperator.Equal, FilterValue.From(true));

        private string RequireProjectedPath(Expression expression) =>
            TryGetProjectedPath(expression, out string? path)
                ? path
                : throw new KernelExpressionException($"Expression '{expression}' is not a filter field.");

        private bool TryGetProjectedPath(Expression expression, out string path)
        {
            expression = StripConvert(expression);
            if (TryGetSubjectFieldPath(expression, out string? sourcePath))
            {
                path = _projectSubjectFields
                    ? ContextProjectionPipeline.ProjectedPath(_pipeline, sourcePath)
                    : sourcePath;
                return true;
            }

            if (Includes.TryTranslate(expression, name: null, out string contextPath))
            {
                path = contextPath;
                return true;
            }

            path = string.Empty;
            return false;
        }

        private bool TryGetSubjectFieldPath(Expression expression, out string fieldPath)
        {
            var names = new Stack<string>();
            ValidateFieldConversion(expression, _subject, Unsupported);
            Expression? current = StripConvert(expression);
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

        private IReadOnlyCollection<FilterValue> ToValues(Expression expression)
        {
            object? value = KernelExpressionEvaluator.Evaluate(expression, _subject, _context);
            if (value is string)
                throw new KernelExpressionException("String constants are scalar values, not filter value lists.");
            if (value is not IEnumerable enumerable)
                throw new KernelExpressionException("Filter value list expression must evaluate to an enumerable.");

            var values = new List<FilterValue>();
            foreach (object? item in enumerable)
                values.Add(FilterValue.FromObject(item) with { ParameterKey = NextParameterKey() });

            return values;
        }

        private FilterValue ToValue(Expression expression, Type? targetType = null)
        {
            object? value = KernelExpressionEvaluator.Evaluate(expression, _subject, _context);
            return FilterValue.FromObject(CoerceValue(value, targetType)) with
            {
                ParameterKey = NextParameterKey(),
            };
        }

        private string NextParameterKey() =>
            "p" + _parameterIndex++;
    }

    private static bool IsKernelIn(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.In));

    private static bool IsKernelExists(MethodInfo method) => IsKernelPredicate(method, nameof(QueryKernelPredicates.Exists));

    private static bool IsKernelPredicate(MethodInfo method, string name) =>
        method.Name == name &&
        method.DeclaringType == typeof(QueryKernelPredicates);

    private static bool IsContains(MethodInfo method) => method.Name is nameof(Enumerable.Contains) or "Contains";

    private static FilterOperator Flip(FilterOperator op) =>
        op switch
        {
            FilterOperator.GreaterThan => FilterOperator.LessThan,
            FilterOperator.GreaterThanOrEqual => FilterOperator.LessThanOrEqual,
            FilterOperator.LessThan => FilterOperator.GreaterThan,
            FilterOperator.LessThanOrEqual => FilterOperator.GreaterThanOrEqual,
            _ => op,
        };

    private static KernelExpressionException Unsupported(Expression expression) =>
        new($"Unsupported server kernel expression '{expression}'.");
}

internal sealed record ContextFilterTranslation(
    FilterExpression Filter,
    EventProjectionInclude[] NewIncludes,
    ContextProjectionBinding[] Bindings);

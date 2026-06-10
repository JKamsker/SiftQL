using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Parameterized;

internal static class ParameterizedFilterPlanBuilder
{
    private const int MaxDepth = 16;
    private const int MaxNodes = 128;
    private const int MaxValues = 128;

    public static ParameterizedFilterPlan Build(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        string[] keys = FilterExpressionParameters.Keys(expression);
        var indexes = keys
            .Select((key, index) => new KeyValuePair<string, int>(key, index))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        int nodes = 0;
        var root = BuildNode(schema, expression, indexes, depth: 0, ref nodes, errorFactory);
        return new ParameterizedFilterPlan(keys, root);
    }

    private static ParameterizedFilterPlanNode BuildNode(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (++nodes > MaxNodes)
            throw Error(errorFactory, $"Filter exceeds the {MaxNodes} node limit.");
        if (depth > MaxDepth)
            throw Error(errorFactory, $"Filter exceeds the {MaxDepth} level depth limit.");

        return expression.Kind switch
        {
            FilterExpressionKind.Any => new ConstantFilterPlanNode(true),
            FilterExpressionKind.And => BuildComposite(schema, expression.Children, indexes, depth, ref nodes, errorFactory, and: true),
            FilterExpressionKind.Or => BuildComposite(schema, expression.Children, indexes, depth, ref nodes, errorFactory, and: false),
            FilterExpressionKind.Not => BuildNot(schema, expression.Children, indexes, depth, ref nodes, errorFactory),
            FilterExpressionKind.Compare => BuildCompare(schema, expression, indexes, errorFactory),
            FilterExpressionKind.In => BuildIn(schema, expression, indexes, errorFactory),
            FilterExpressionKind.Exists => BuildExists(schema, expression, errorFactory),
            FilterExpressionKind.Contains => BuildContains(schema, expression, indexes, errorFactory),
            FilterExpressionKind.Count => BuildCount(schema, expression, indexes, errorFactory),
            FilterExpressionKind.Between => BuildBetween(schema, expression, indexes, errorFactory),
            FilterExpressionKind.ElemMatch => BuildElemMatch(schema, expression, indexes, depth, ref nodes, errorFactory),
            _ => throw Error(errorFactory, $"Unknown filter node kind '{expression.Kind}'."),
        };
    }

    private static ParameterizedFilterPlanNode BuildComposite(
        FilterSchema schema,
        FilterExpression[] children,
        IReadOnlyDictionary<string, int> indexes,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory,
        bool and)
    {
        if (children.Length == 0)
            throw Error(errorFactory, "Composite filters must have at least one child.");
        FilterExpression[] ordered = and && children.Length > 1
            ? children.OrderBy(FilterExpressionCost.Estimate).ToArray()
            : children;
        var compiled = new ParameterizedFilterPlanNode[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
            compiled[i] = BuildNode(schema, ordered[i], indexes, depth + 1, ref nodes, errorFactory);
        return new CompositeFilterPlanNode(compiled, and);
    }

    private static ParameterizedFilterPlanNode BuildNot(
        FilterSchema schema,
        FilterExpression[] children,
        IReadOnlyDictionary<string, int> indexes,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (children.Length != 1)
            throw Error(errorFactory, "Not filters must have exactly one child.");
        return new NotFilterPlanNode(BuildNode(schema, children[0], indexes, depth + 1, ref nodes, errorFactory));
    }

    private static ParameterizedFilterPlanNode BuildCompare(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (FilterNullCheck.IsPresenceCheck(field, expression))
        {
            ParameterizedFilterPlanNode exists = new ExistsFilterPlanNode(field);
            return FilterNullCheck.MatchesPresent(expression) ? exists : new NotFilterPlanNode(exists);
        }

        EnsureScalar(field, errorFactory);
        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        FilterValues.ValidateComparison(field, expression.Operator, value, errorFactory, expression.IgnoreCase);
        return new CompareFilterPlanNode(field, expression.Operator, FilterValueRef.Create(value, indexes), expression.IgnoreCase);
    }

    private static ParameterizedFilterPlanNode BuildBetween(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        if (expression.Values.Length != 2)
            throw Error(errorFactory, $"Between filters on '{field.Name}' require exactly two values.");

        FilterValue lower = expression.Values[0];
        FilterValue upper = expression.Values[1];
        FilterValues.ValidateComparison(field, FilterOperator.GreaterThanOrEqual, lower, errorFactory);
        FilterValues.ValidateComparison(field, FilterOperator.LessThanOrEqual, upper, errorFactory);
        // Literal bounds must be ordered; reversed bounds describe an empty interval.
        // Parameterized bounds are left unchecked (their values are not known here).
        if (lower.ParameterKey is null && upper.ParameterKey is null &&
            FilterValues.TryCompareValues(lower, upper, out int order) && order > 0)
        {
            throw Error(errorFactory, $"Between filters on '{field.Name}' require the lower bound to be <= the upper bound.");
        }

        return new BetweenFilterPlanNode(
            field,
            FilterValueRef.Create(lower, indexes),
            FilterValueRef.Create(upper, indexes));
    }

    private static ParameterizedFilterPlanNode BuildElemMatch(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Children.Length != 1)
            throw Error(errorFactory, "ElemMatch filters must have exactly one child.");
        if (!ElementCollection.TryResolve(
                schema.SubjectType,
                expression.Field,
                out Func<object, System.Collections.IEnumerable?> getCollection,
                out Type elementType))
        {
            throw Error(errorFactory, $"Filter field '{expression.Field}' is not an element collection.");
        }

        // The child binds against the same parameter array, so it keeps the
        // global indexes while resolving its fields against the element schema.
        FilterSchema elementSchema = FilterSchema.For(elementType);
        ParameterizedFilterPlanNode child = BuildNode(
            elementSchema,
            expression.Children[0],
            indexes,
            depth + 1,
            ref nodes,
            errorFactory);
        return new ElemMatchFilterPlanNode(getCollection, child);
    }

    private static ParameterizedFilterPlanNode BuildCount(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (field.Kind != FilterFieldKind.Array)
            throw Error(errorFactory, $"Filter field '{field.Name}' is not a collection.");
        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        if (value.Kind is not (FilterValueKind.Integer or FilterValueKind.UnsignedInteger))
            throw Error(errorFactory, $"Count comparisons on '{field.Name}' require an integer value.");
        return new CountFilterPlanNode(field, expression.Operator, FilterValueRef.Create(value, indexes));
    }

    private static ParameterizedFilterPlanNode BuildIn(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        ValidateValues(field, expression.Values, errorFactory);
        return new InFilterPlanNode(
            field,
            expression.Values.Select(value => FilterValueRef.Create(value, indexes)).ToArray());
    }

    private static ParameterizedFilterPlanNode BuildExists(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory) =>
        new ExistsFilterPlanNode(RequireField(schema, expression.Field, errorFactory));

    private static ParameterizedFilterPlanNode BuildContains(
        FilterSchema schema,
        FilterExpression expression,
        IReadOnlyDictionary<string, int> indexes,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (field.Kind != FilterFieldKind.Array && field.ValueType != typeof(ProjectedEventValue))
            throw Error(errorFactory, $"Filter field '{field.Name}' is not a scalar array.");
        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        FilterValues.ValidateValue(field, value, errorFactory);
        return new ContainsFilterPlanNode(field, FilterValueRef.Create(value, indexes));
    }

    private static FilterField RequireField(
        FilterSchema schema,
        string fieldName,
        Func<string, Exception>? errorFactory) =>
        schema.TryGetField(fieldName, out var field)
            ? field
            : throw Error(errorFactory, $"Filter field '{fieldName}' is not supported by {schema.SubjectType.FullName}.");

    private static void EnsureScalar(FilterField field, Func<string, Exception>? errorFactory)
    {
        if (field.Kind != FilterFieldKind.Scalar)
            throw Error(errorFactory, $"Filter field '{field.Name}' is not scalar.");
    }

    private static void ValidateValues(
        FilterField field,
        FilterValue[] values,
        Func<string, Exception>? errorFactory)
    {
        if (values.Length == 0 || values.Length > MaxValues)
            throw Error(errorFactory, $"Filter field '{field.Name}' must have between 1 and {MaxValues} values.");
        for (int i = 0; i < values.Length; i++)
            FilterValues.ValidateValue(field, values[i], errorFactory);
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

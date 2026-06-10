using SiftQL;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterInterpretedCompiler
{
    private const int MaxDepth = 16;
    private const int MaxNodes = 128;
    private const int MaxValues = 128;
    private const int MaxRuntimeElements = 256;

    public static Func<object, bool> Compile(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        int nodes = 0;
        return CompileNode(schema, expression, depth: 0, ref nodes, errorFactory);
    }

    private static Func<object, bool> CompileNode(
        FilterSchema schema,
        FilterExpression expression,
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
            FilterExpressionKind.Any => static _ => true,
            FilterExpressionKind.And => CompileAll(schema, expression.Children, depth, ref nodes, errorFactory),
            FilterExpressionKind.Or => CompileAny(schema, expression.Children, depth, ref nodes, errorFactory),
            FilterExpressionKind.Not => CompileNot(schema, expression.Children, depth, ref nodes, errorFactory),
            FilterExpressionKind.Compare => CompileCompare(schema, expression, errorFactory),
            FilterExpressionKind.In => CompileIn(schema, expression, errorFactory),
            FilterExpressionKind.Exists => CompileExists(schema, expression, errorFactory),
            FilterExpressionKind.Contains => CompileContains(schema, expression, errorFactory),
            FilterExpressionKind.Count => CompileCount(schema, expression, errorFactory),
            FilterExpressionKind.Between => CompileBetween(schema, expression, errorFactory),
            FilterExpressionKind.ElemMatch => CompileElemMatch(schema, expression, errorFactory),
            _ => throw Error(errorFactory, $"Unknown filter node kind '{expression.Kind}'."),
        };
    }

    private static Func<object, bool> CompileAll(
        FilterSchema schema,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        FilterExpression[] ordered = children.Length > 1
            ? children.OrderBy(FilterExpressionCost.Estimate).ToArray()
            : children;
        var compiled = CompileChildren(schema, ordered, depth, ref nodes, errorFactory);
        return subject =>
        {
            for (int i = 0; i < compiled.Length; i++)
            {
                if (!compiled[i](subject))
                    return false;
            }

            return true;
        };
    }

    private static Func<object, bool> CompileAny(
        FilterSchema schema,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        var compiled = CompileChildren(schema, children, depth, ref nodes, errorFactory);
        return subject =>
        {
            for (int i = 0; i < compiled.Length; i++)
            {
                if (compiled[i](subject))
                    return true;
            }

            return false;
        };
    }

    private static Func<object, bool> CompileNot(
        FilterSchema schema,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (children.Length != 1)
            throw Error(errorFactory, "Not filters must have exactly one child.");

        var child = CompileNode(schema, children[0], depth + 1, ref nodes, errorFactory);
        return subject => !child(subject);
    }

    private static Func<object, bool>[] CompileChildren(
        FilterSchema schema,
        FilterExpression[] children,
        int depth,
        ref int nodes,
        Func<string, Exception>? errorFactory)
    {
        if (children.Length == 0)
            throw Error(errorFactory, "Composite filters must have at least one child.");

        var compiled = new Func<object, bool>[children.Length];
        for (int i = 0; i < children.Length; i++)
            compiled[i] = CompileNode(schema, children[i], depth + 1, ref nodes, errorFactory);
        return compiled;
    }

    private static Func<object, bool> CompileCompare(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        bool ignoreCase = expression.IgnoreCase;
        FilterValues.ValidateComparison(field, expression.Operator, value, errorFactory, ignoreCase);
        var typed = FilterTypedPredicates.TryCompileCompare(field, value, expression.Operator, ignoreCase);
        return typed ?? (subject => FilterValues.Compare(field.Getter(subject), value, expression.Operator, ignoreCase));
    }

    private static Func<object, bool> CompileIn(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        EnsureScalar(field, errorFactory);
        FilterValue[] values = expression.Values.ToArray();
        ValidateValues(field, values, errorFactory);
        var typed = FilterTypedPredicates.TryCompileIn(field, values);
        return typed ?? (subject => FilterValues.In(field.Getter(subject), values));
    }

    private static Func<object, bool> CompileExists(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        return subject => field.Getter(subject) is not null;
    }

    private static Func<object, bool> CompileContains(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (field.Kind != FilterFieldKind.Array && field.ValueType != typeof(ProjectedEventValue))
            throw Error(errorFactory, $"Filter field '{field.Name}' is not a scalar array.");

        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        FilterValues.ValidateValue(field, value, errorFactory);
        var typed = FilterTypedArrayPredicates.TryCompileContains(field, value);
        return typed ?? (subject => FilterValues.Contains(field.Getter(subject), value));
    }

    private static Func<object, bool> CompileBetween(
        FilterSchema schema,
        FilterExpression expression,
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

        return subject =>
        {
            object? actual = field.Getter(subject);
            return FilterValues.Compare(actual, lower, FilterOperator.GreaterThanOrEqual) &&
                FilterValues.Compare(actual, upper, FilterOperator.LessThanOrEqual);
        };
    }

    private static Func<object, bool> CompileElemMatch(
        FilterSchema schema,
        FilterExpression expression,
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

        FilterSchema elementSchema = FilterSchema.For(elementType);
        Func<object, bool> child = Compile(elementSchema, expression.Children[0], errorFactory);
        return subject =>
        {
            System.Collections.IEnumerable? collection = getCollection(subject);
            if (collection is null)
                return false;

            int seen = 0;
            foreach (object? element in collection)
            {
                if (++seen > MaxRuntimeElements)
                    throw new InvalidOperationException(
                        $"ElemMatch filters support at most {MaxRuntimeElements} elements.");
                if (element is not null && child(element))
                    return true;
            }

            return false;
        };
    }

    private static Func<object, bool> CompileCount(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        FilterField field = RequireField(schema, expression.Field, errorFactory);
        if (field.Kind != FilterFieldKind.Array)
            throw Error(errorFactory, $"Filter field '{field.Name}' is not a collection.");

        FilterValue value = expression.Value ??
            throw Error(errorFactory, $"Filter field '{expression.Field}' is missing a value.");
        if (value.Kind is not (FilterValueKind.Integer or FilterValueKind.UnsignedInteger))
            throw Error(errorFactory, $"Count comparisons on '{field.Name}' require an integer value.");

        FilterOperator op = expression.Operator;
        return subject => FilterValues.Compare(FilterValues.Count(field.Getter(subject)), value, op);
    }

    private static FilterField RequireField(
        FilterSchema schema,
        string fieldName,
        Func<string, Exception>? errorFactory) =>
        schema.TryGetField(fieldName, out var field)
            ? field
            : throw Error(
                errorFactory,
                $"Filter field '{fieldName}' is not supported by {schema.SubjectType.FullName}.");

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
        {
            throw Error(
                errorFactory,
                $"Filter field '{field.Name}' must have between 1 and {MaxValues} values.");
        }

        for (int i = 0; i < values.Length; i++)
            FilterValues.ValidateValue(field, values[i], errorFactory);
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

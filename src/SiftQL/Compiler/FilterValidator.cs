using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Compiler;

public sealed record FilterLimits
{
    public int MaxDepth { get; init; } = 16;
    public int MaxNodes { get; init; } = 128;
    public int MaxValues { get; init; } = 128;
    public int MaxBytes { get; init; } = 64 * 1024;

    public static FilterLimits Default { get; } = new();
}

public sealed record FilterValidationError(string Path, string Message);

public sealed record FilterValidationResult(bool IsValid, IReadOnlyList<FilterValidationError> Errors);

// Validates an untrusted filter against a subject's schema, aggregating every
// error with a JSON-path-ish locator and enforcing configurable complexity
// limits -- decoupled from compilation and caching. Unlike FilterCompiler.Compile
// (which throws on the first error), this reports all problems at once.
public static class FilterValidator
{
    public static FilterValidationResult Validate(
        Type subjectType,
        FilterExpression? filter,
        FilterLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        limits ??= FilterLimits.Default;

        FilterSchema schema = FilterSchema.For(subjectType);
        var errors = new List<FilterValidationError>();
        int nodes = 0;
        Walk(schema, filter ?? FilterExpression.Any, "$", depth: 0, ref nodes, limits, errors);
        return new FilterValidationResult(errors.Count == 0, errors);
    }

    public static FilterValidationResult Validate(
        Type subjectType,
        string json,
        FilterLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        limits ??= FilterLimits.Default;

        if (json is null)
            return Invalid("$", "Filter JSON is null.");
        if (System.Text.Encoding.UTF8.GetByteCount(json) > limits.MaxBytes)
            return Invalid("$", $"Filter JSON exceeds the {limits.MaxBytes}-byte limit.");

        FilterExpression filter;
        try
        {
            filter = FilterDocument.Deserialize(json);
        }
        catch (FilterSerializationException ex)
        {
            return Invalid("$", ex.Message);
        }

        return Validate(subjectType, filter, limits);
    }

    private static FilterValidationResult Invalid(string path, string message) =>
        new(false, [new FilterValidationError(path, message)]);

    private static void Walk(
        FilterSchema schema,
        FilterExpression node,
        string path,
        int depth,
        ref int nodes,
        FilterLimits limits,
        List<FilterValidationError> errors)
    {
        nodes++;
        if (depth > limits.MaxDepth)
        {
            errors.Add(new FilterValidationError(path, $"Filter exceeds the {limits.MaxDepth} level depth limit."));
            return;
        }

        if (nodes > limits.MaxNodes)
        {
            errors.Add(new FilterValidationError(path, $"Filter exceeds the {limits.MaxNodes} node limit."));
            return;
        }

        switch (node.Kind)
        {
            case FilterExpressionKind.Any:
                break;
            case FilterExpressionKind.And:
            case FilterExpressionKind.Or:
                WalkChildren(schema, node, path, depth, ref nodes, limits, errors, requireExactlyOne: false);
                break;
            case FilterExpressionKind.Not:
                WalkChildren(schema, node, path, depth, ref nodes, limits, errors, requireExactlyOne: true);
                break;
            case FilterExpressionKind.Compare:
                ValidateCompare(schema, node, path, errors);
                break;
            case FilterExpressionKind.In:
                ValidateIn(schema, node, path, limits, errors);
                break;
            case FilterExpressionKind.Contains:
                ValidateCollectionLeaf(schema, node, path, errors, requireValue: true);
                break;
            case FilterExpressionKind.Count:
                ValidateCount(schema, node, path, errors);
                break;
            case FilterExpressionKind.Between:
                ValidateBetween(schema, node, path, errors);
                break;
            case FilterExpressionKind.ElemMatch:
                ValidateElemMatch(schema, node, path, depth, ref nodes, limits, errors);
                break;
            case FilterExpressionKind.Exists:
                RequireField(schema, node.Field, path, errors, out _);
                break;
            default:
                errors.Add(new FilterValidationError(path, $"Unknown filter node kind '{node.Kind}'."));
                break;
        }
    }

    private static void WalkChildren(
        FilterSchema schema,
        FilterExpression node,
        string path,
        int depth,
        ref int nodes,
        FilterLimits limits,
        List<FilterValidationError> errors,
        bool requireExactlyOne)
    {
        if (requireExactlyOne && node.Children.Length != 1)
            errors.Add(new FilterValidationError(path, "Not filters must have exactly one child."));
        else if (!requireExactlyOne && node.Children.Length == 0)
            errors.Add(new FilterValidationError(path, "Composite filters must have at least one child."));

        for (int i = 0; i < node.Children.Length; i++)
            Walk(schema, node.Children[i], $"{path}.children[{i}]", depth + 1, ref nodes, limits, errors);
    }

    private static void ValidateCompare(
        FilterSchema schema,
        FilterExpression node,
        string path,
        List<FilterValidationError> errors)
    {
        if (!RequireField(schema, node.Field, path, errors, out FilterField? field))
            return;
        if (node.Value is null)
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is missing a value."));
            return;
        }

        // Object/array member presence checks (field ==/!= null) are valid regardless
        // of scalar kind; they lower to Exists / Not(Exists) at compile time.
        if (FilterNullCheck.IsPresenceCheck(field!, node))
            return;

        if (field!.Kind != FilterFieldKind.Scalar)
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is not scalar."));
            return;
        }

        Capture(path, errors, () =>
            FilterValues.ValidateComparison(field!, node.Operator, node.Value, Signal, node.IgnoreCase));
    }

    private static void ValidateIn(
        FilterSchema schema,
        FilterExpression node,
        string path,
        FilterLimits limits,
        List<FilterValidationError> errors)
    {
        if (!RequireScalarField(schema, node.Field, path, errors, out FilterField? field))
            return;
        if (node.Values.Length == 0 || node.Values.Length > limits.MaxValues)
        {
            errors.Add(new FilterValidationError(
                path,
                $"Filter field '{node.Field}' must have between 1 and {limits.MaxValues} values."));
            return;
        }

        for (int i = 0; i < node.Values.Length; i++)
        {
            int index = i;
            Capture($"{path}.values[{index}]", errors, () =>
                FilterValues.ValidateValue(field!, node.Values[index], Signal));
        }
    }

    private static void ValidateCollectionLeaf(
        FilterSchema schema,
        FilterExpression node,
        string path,
        List<FilterValidationError> errors,
        bool requireValue)
    {
        if (!RequireField(schema, node.Field, path, errors, out FilterField? field))
            return;
        if (field!.Kind != FilterFieldKind.Array)
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is not a collection."));
            return;
        }

        if (requireValue && node.Value is null)
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is missing a value."));
        else if (node.Value is not null)
            Capture(path, errors, () => FilterValues.ValidateValue(field, node.Value, Signal));
    }

    private static void ValidateCount(
        FilterSchema schema,
        FilterExpression node,
        string path,
        List<FilterValidationError> errors)
    {
        if (!RequireField(schema, node.Field, path, errors, out FilterField? field))
            return;
        if (field!.Kind != FilterFieldKind.Array && field.ValueType != typeof(ProjectedEventValue))
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is not a collection."));
            return;
        }

        if (node.Value is null ||
            node.Value.Kind is not (FilterValueKind.Integer or FilterValueKind.UnsignedInteger))
        {
            errors.Add(new FilterValidationError(path, $"Count comparisons on '{node.Field}' require an integer value."));
        }

        if (node.Operator is not (FilterOperator.Equal or
            FilterOperator.NotEqual or
            FilterOperator.GreaterThan or
            FilterOperator.GreaterThanOrEqual or
            FilterOperator.LessThan or
            FilterOperator.LessThanOrEqual))
        {
            errors.Add(new FilterValidationError(path, $"Count comparisons on '{node.Field}' require a comparison operator."));
        }
    }

    private static void ValidateBetween(
        FilterSchema schema,
        FilterExpression node,
        string path,
        List<FilterValidationError> errors)
    {
        if (!RequireScalarField(schema, node.Field, path, errors, out FilterField? field))
            return;
        if (node.Values.Length != 2)
        {
            errors.Add(new FilterValidationError(path, $"Between filters on '{node.Field}' require exactly two values."));
            return;
        }

        for (int i = 0; i < node.Values.Length; i++)
        {
            int index = i;
            FilterOperator boundOperator = index == 0
                ? FilterOperator.GreaterThanOrEqual
                : FilterOperator.LessThanOrEqual;
            Capture($"{path}.values[{index}]", errors, () =>
                FilterValues.ValidateComparison(field!, boundOperator, node.Values[index], Signal));
        }

        FilterValue lower = node.Values[0];
        FilterValue upper = node.Values[1];
        if (string.IsNullOrWhiteSpace(lower.ParameterKey) &&
            string.IsNullOrWhiteSpace(upper.ParameterKey) &&
            FilterValues.TryCompareValues(lower, upper, out int order) && order > 0)
        {
            errors.Add(new FilterValidationError(
                path,
                $"Between filters on '{node.Field}' require the lower bound to be <= the upper bound."));
        }
    }

    private static void ValidateElemMatch(
        FilterSchema schema,
        FilterExpression node,
        string path,
        int depth,
        ref int nodes,
        FilterLimits limits,
        List<FilterValidationError> errors)
    {
        if (node.Children.Length != 1)
        {
            errors.Add(new FilterValidationError(path, "ElemMatch filters must have exactly one child."));
            return;
        }

        // The child's fields are relative to the element, so it must be validated
        // against the element schema -- mirrors FilterInterpretedCompiler.CompileElemMatch.
        if (!ElementCollection.TryResolve(schema.SubjectType, node.Field, out _, out Type elementType))
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{node.Field}' is not an element collection."));
            return;
        }

        FilterSchema elementSchema = FilterSchema.For(elementType);
        Walk(elementSchema, node.Children[0], $"{path}.children[0]", depth + 1, ref nodes, limits, errors);
    }

    private static bool RequireScalarField(
        FilterSchema schema,
        string fieldName,
        string path,
        List<FilterValidationError> errors,
        out FilterField? field)
    {
        if (!RequireField(schema, fieldName, path, errors, out field))
            return false;
        if (field!.Kind != FilterFieldKind.Scalar)
        {
            errors.Add(new FilterValidationError(path, $"Filter field '{fieldName}' is not scalar."));
            return false;
        }

        return true;
    }

    private static bool RequireField(
        FilterSchema schema,
        string fieldName,
        string path,
        List<FilterValidationError> errors,
        out FilterField? field)
    {
        if (schema.TryGetField(fieldName, out FilterField? resolved))
        {
            field = resolved;
            return true;
        }

        errors.Add(new FilterValidationError(
            path,
            $"Filter field '{fieldName}' is not supported by {schema.SubjectType.FullName}."));
        field = null;
        return false;
    }

    private static void Capture(string path, List<FilterValidationError> errors, Action validate)
    {
        try
        {
            validate();
        }
        catch (ValidationSignal signal)
        {
            errors.Add(new FilterValidationError(path, signal.Message));
        }
    }

    private static Exception Signal(string message) => new ValidationSignal(message);

    private sealed class ValidationSignal(string message) : Exception(message);
}

using System.Text.Json.Serialization;
using SiftQL.Translation;

namespace SiftQL.Expressions;

public enum FilterExpressionKind
{
    Any = 0,
    And = 1,
    Or = 2,
    Not = 3,
    Compare = 4,
    In = 5,
    Exists = 6,
    Contains = 7,
    Count = 8,
    ElemMatch = 9,
    Between = 10,
}

public enum FilterOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    StringContains = 6,
    StringStartsWith = 7,
    StringEndsWith = 8,
}

public sealed record FilterExpression
{
    public static FilterExpression Any { get; } = new(FilterExpressionKind.Any);

    // Default record equality compares Children/Values arrays by reference, so
    // composites built twice never compare equal. These members provide
    // canonicalizing, value-based structural identity for deduping stored or
    // transmitted filters in memory (StructuralComparer) or by stable key
    // (ContentSignature). See [[FilterExpressionCanonical]].
    public static IEqualityComparer<FilterExpression> StructuralComparer =>
        FilterExpressionCanonical.Comparer;

    public static FilterExpression Canonicalize(FilterExpression filter) =>
        FilterExpressionCanonical.Canonicalize(filter);

    public static string ContentSignature(FilterExpression filter) =>
        FilterExpressionCanonical.Signature(filter);

    // Always-false sentinel (Not(Any)); the simplifier collapses unsatisfiable
    // filters to this. See [[FilterExpressionCanonical]].
    public static FilterExpression Never => FilterExpressionCanonical.Never;

    public static FilterExpression Simplify(FilterExpression filter) =>
        FilterExpressionCanonical.Simplify(filter);

    public static bool IsAlwaysTrue(FilterExpression filter) =>
        FilterExpressionCanonical.IsAlwaysTrue(filter);

    public static bool IsAlwaysFalse(FilterExpression filter) =>
        FilterExpressionCanonical.IsAlwaysFalse(filter);

    public static bool IsSatisfiable(FilterExpression filter) =>
        !FilterExpressionCanonical.IsAlwaysFalse(filter);

    public FilterExpression()
    {
    }

    public FilterExpression(FilterExpressionKind kind)
    {
        Kind = kind;
    }

    public FilterExpressionKind Kind { get; init; }
    public string Field { get; init; } = string.Empty;
    public FilterOperator Operator { get; init; }
    public FilterValue? Value { get; init; }
    public FilterValue[] Values { get; init; } = [];
    public FilterExpression[] Children { get; init; } = [];

    // Case-insensitive (OrdinalIgnoreCase) matching for string operators
    // (Equal, NotEqual, StringContains, StringStartsWith, StringEndsWith).
    // Serialized only when true so ordinal filters keep their existing wire
    // format and compile fingerprints.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IgnoreCase { get; init; }

    public static FilterExpression Compare(
        string field,
        FilterOperator op,
        FilterValue value,
        bool ignoreCase = false) =>
        new(FilterExpressionKind.Compare)
        {
            Field = RequireField(field),
            Operator = op,
            Value = value ?? throw new ArgumentNullException(nameof(value)),
            IgnoreCase = ignoreCase,
        };

    public static FilterExpression In(string field, IReadOnlyCollection<FilterValue> values) =>
        new(FilterExpressionKind.In)
        {
            Field = RequireField(field),
            Values = ToValues(values),
        };

    // Inclusive numeric/temporal range: lower &lt;= field &lt;= upper, as a single
    // node. Values are [lower, upper].
    public static FilterExpression Between(string field, FilterValue lower, FilterValue upper) =>
        new(FilterExpressionKind.Between)
        {
            Field = RequireField(field),
            Values =
            [
                lower ?? throw new ArgumentNullException(nameof(lower)),
                upper ?? throw new ArgumentNullException(nameof(upper)),
            ],
        };

    public static FilterExpression Contains(string field, FilterValue value) =>
        new(FilterExpressionKind.Contains)
        {
            Field = RequireField(field),
            Value = value ?? throw new ArgumentNullException(nameof(value)),
        };

    public static FilterExpression StringContains(string field, FilterValue value, bool ignoreCase = false) =>
        Compare(field, FilterOperator.StringContains, value, ignoreCase);

    public static FilterExpression StringStartsWith(string field, FilterValue value, bool ignoreCase = false) =>
        Compare(field, FilterOperator.StringStartsWith, value, ignoreCase);

    public static FilterExpression StringEndsWith(string field, FilterValue value, bool ignoreCase = false) =>
        Compare(field, FilterOperator.StringEndsWith, value, ignoreCase);

    public static FilterExpression Exists(string field) =>
        new(FilterExpressionKind.Exists) { Field = RequireField(field) };

    // Compares the element count of a collection field against an integer value
    // (e.g. Items.Count() > 0). Field is the collection; Operator/Value compare
    // its cardinality.
    public static FilterExpression Count(string field, FilterOperator op, FilterValue value) =>
        new(FilterExpressionKind.Count)
        {
            Field = RequireField(field),
            Operator = op,
            Value = value ?? throw new ArgumentNullException(nameof(value)),
        };

    // Matches a collection where at least one element satisfies the whole child
    // filter (correlated, MongoDB $elemMatch semantics). Field is the collection
    // path; the single child's fields are relative to the element.
    public static FilterExpression ElemMatch(string field, FilterExpression child) =>
        new(FilterExpressionKind.ElemMatch)
        {
            Field = RequireField(field),
            Children = [child ?? throw new ArgumentNullException(nameof(child))],
        };

    public static FilterExpression Not(FilterExpression child) =>
        new(FilterExpressionKind.Not)
        {
            Children = [child ?? throw new ArgumentNullException(nameof(child))],
        };

    public static FilterExpression And(params FilterExpression[] children) =>
        CombineAnd(children);

    public static FilterExpression Or(params FilterExpression[] children) =>
        CombineOr(children);

    private static FilterExpression CombineAnd(IReadOnlyCollection<FilterExpression> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var filtered = children
            .Select(static child => child ?? throw new ArgumentException(
                "Composite filters cannot contain null children.",
                nameof(children)))
            .Where(static child => child.Kind != FilterExpressionKind.Any)
            .ToArray();

        return filtered.Length switch
        {
            0 => Any,
            1 => filtered[0],
            _ => new FilterExpression(FilterExpressionKind.And) { Children = filtered },
        };
    }

    private static FilterExpression CombineOr(IReadOnlyCollection<FilterExpression> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var filtered = children
            .Select(static child => child ?? throw new ArgumentException(
                "Composite filters cannot contain null children.",
                nameof(children)))
            .ToArray();
        if (filtered.Length == 0)
            throw new ArgumentException("Or filters must contain at least one child.", nameof(children));

        if (filtered.Any(static child => child.Kind == FilterExpressionKind.Any))
            return Any;

        return filtered.Length switch
        {
            1 => filtered[0],
            _ => new FilterExpression(FilterExpressionKind.Or) { Children = filtered },
        };
    }

    private static FilterValue[] ToValues(IReadOnlyCollection<FilterValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("Filter value lists cannot be empty.", nameof(values));
        }

        var items = values.ToArray();
        if (items.Any(static value => value is null))
            throw new ArgumentException("Filter value lists cannot contain null values.", nameof(values));

        return items;
    }

    private static string RequireField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return field;
    }
}

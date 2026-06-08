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
}

public sealed record FilterExpression
{
    public static FilterExpression Any { get; } = new(FilterExpressionKind.Any);

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

    public static FilterExpression Compare(
        string field,
        FilterOperator op,
        FilterValue value) =>
        new(FilterExpressionKind.Compare)
        {
            Field = RequireField(field),
            Operator = op,
            Value = value ?? throw new ArgumentNullException(nameof(value)),
        };

    public static FilterExpression In(string field, IReadOnlyCollection<FilterValue> values) =>
        new(FilterExpressionKind.In)
        {
            Field = RequireField(field),
            Values = ToValues(values),
        };

    public static FilterExpression Contains(string field, FilterValue value) =>
        new(FilterExpressionKind.Contains)
        {
            Field = RequireField(field),
            Value = value ?? throw new ArgumentNullException(nameof(value)),
        };

    public static FilterExpression StringContains(string field, FilterValue value) =>
        Compare(field, FilterOperator.StringContains, value);

    public static FilterExpression Exists(string field) =>
        new(FilterExpressionKind.Exists) { Field = RequireField(field) };

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
        if (filtered.Any(static child => child.Kind == FilterExpressionKind.Any))
            return Any;

        return filtered.Length switch
        {
            0 => Any,
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

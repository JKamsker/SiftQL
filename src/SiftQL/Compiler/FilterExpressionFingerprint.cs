using System.Text;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterExpressionFingerprint
{
    public static FilterExpressionKey CreateKey(FilterExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return FilterExpressionKey.From(expression);
    }

    public static string Create(FilterExpression expression) =>
        CreateKey(expression).ToString();
}

internal sealed class FilterExpressionKey : IEquatable<FilterExpressionKey>
{
    private readonly int _hashCode;

    private FilterExpressionKey(
        FilterExpressionKind kind,
        string field,
        FilterOperator op,
        bool ignoreCase,
        FilterValueKey value,
        StructuralKeyArray<FilterValueKey> values,
        StructuralKeyArray<FilterExpressionKey> children)
    {
        Kind = kind;
        Field = field;
        Operator = op;
        IgnoreCase = ignoreCase;
        Value = value;
        Values = values;
        Children = children;
        _hashCode = HashCode.Combine(Kind, Field, Operator, IgnoreCase, Value, Values, Children);
    }

    public FilterExpressionKind Kind { get; }
    public string Field { get; }
    public FilterOperator Operator { get; }
    public bool IgnoreCase { get; }
    public FilterValueKey Value { get; }
    public StructuralKeyArray<FilterValueKey> Values { get; }
    public StructuralKeyArray<FilterExpressionKey> Children { get; }

    public static FilterExpressionKey From(FilterExpression expression) =>
        new(
            expression.Kind,
            FieldForKey(expression),
            OperatorForKey(expression),
            IgnoreCaseForKey(expression),
            ValueForKey(expression),
            ValuesForKey(expression),
            ChildrenForKey(expression));

    private static string FieldForKey(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.Compare or
            FilterExpressionKind.In or
            FilterExpressionKind.Exists or
            FilterExpressionKind.Contains or
            FilterExpressionKind.Count or
            FilterExpressionKind.ElemMatch
            ? expression.Field
            : string.Empty;

    private static FilterOperator OperatorForKey(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.Compare or FilterExpressionKind.Count
            ? expression.Operator
            : default;

    private static bool IgnoreCaseForKey(FilterExpression expression) =>
        expression.Kind == FilterExpressionKind.Compare && expression.IgnoreCase;

    private static FilterValueKey ValueForKey(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.Compare or
            FilterExpressionKind.Contains or
            FilterExpressionKind.Count
            ? FilterValueKey.From(expression.Value)
            : default;

    private static StructuralKeyArray<FilterValueKey> ValuesForKey(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.In or FilterExpressionKind.Between
            ? StructuralKeyArray<FilterValueKey>.From(expression.Values, FilterValueKey.From)
            : StructuralKeyArray<FilterValueKey>.Empty;

    private static StructuralKeyArray<FilterExpressionKey> ChildrenForKey(FilterExpression expression) =>
        expression.Kind is FilterExpressionKind.ElemMatch or
            FilterExpressionKind.And or
            FilterExpressionKind.Or or
            FilterExpressionKind.Not
            ? StructuralKeyArray<FilterExpressionKey>.From(expression.Children, From)
            : StructuralKeyArray<FilterExpressionKey>.Empty;

    public bool Equals(FilterExpressionKey? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
            Kind == other.Kind &&
            string.Equals(Field, other.Field, StringComparison.Ordinal) &&
            Operator == other.Operator &&
            IgnoreCase == other.IgnoreCase &&
            Value.Equals(other.Value) &&
            Values.Equals(other.Values) &&
            Children.Equals(other.Children));

    public override bool Equals(object? obj) =>
        obj is FilterExpressionKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    private void AppendTo(StringBuilder builder)
    {
        builder.Append('(').Append((int)Kind);
        switch (Kind)
        {
            case FilterExpressionKind.Compare:
                FilterKeyText.AppendText(builder, Field);
                builder.Append(':').Append((int)Operator);
                if (IgnoreCase)
                    builder.Append('i');
                Value.AppendTo(builder);
                break;
            case FilterExpressionKind.In:
            case FilterExpressionKind.Between:
                FilterKeyText.AppendText(builder, Field);
                AppendValues(builder);
                break;
            case FilterExpressionKind.Exists:
                FilterKeyText.AppendText(builder, Field);
                break;
            case FilterExpressionKind.Contains:
                FilterKeyText.AppendText(builder, Field);
                Value.AppendTo(builder);
                break;
            case FilterExpressionKind.Count:
                FilterKeyText.AppendText(builder, Field);
                builder.Append(':').Append((int)Operator);
                Value.AppendTo(builder);
                break;
            case FilterExpressionKind.ElemMatch:
                FilterKeyText.AppendText(builder, Field);
                AppendChildren(builder);
                break;
            case FilterExpressionKind.And:
            case FilterExpressionKind.Or:
            case FilterExpressionKind.Not:
                AppendChildren(builder);
                break;
        }

        builder.Append(')');
    }

    private void AppendChildren(StringBuilder builder)
    {
        builder.Append('[').Append(Children.Count).Append(']');
        for (int i = 0; i < Children.Count; i++)
            Children[i].AppendTo(builder);
    }

    private void AppendValues(StringBuilder builder)
    {
        builder.Append('[').Append(Values.Count).Append(']');
        for (int i = 0; i < Values.Count; i++)
            Values[i].AppendTo(builder);
    }
}

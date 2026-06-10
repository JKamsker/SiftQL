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
            expression.Field,
            expression.Operator,
            expression.IgnoreCase,
            FilterValueKey.From(expression.Value),
            expression.Values.Length == 0
                ? StructuralKeyArray<FilterValueKey>.Empty
                : StructuralKeyArray<FilterValueKey>.From(expression.Values, FilterValueKey.From),
            expression.Children.Length == 0
                ? StructuralKeyArray<FilterExpressionKey>.Empty
                : StructuralKeyArray<FilterExpressionKey>.From(expression.Children, From));

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

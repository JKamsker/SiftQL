using System.Globalization;
using System.Text;

namespace SiftQL.Expressions;

// Produces a canonical form and a stable, process-independent content signature
// for a FilterExpression. Two filters that are semantically equal -- modulo
// And/Or child ordering, duplicate siblings, In-value ordering, and redundant
// Any children -- share a signature and compare equal under StructuralComparer.
//
// The default record equality on FilterExpression compares Children/Values
// arrays by reference, so composites built twice never compare equal; this type
// is the supported way to dedupe stored or transmitted filters.
internal static class FilterExpressionCanonical
{
    public static readonly IEqualityComparer<FilterExpression> Comparer = new StructuralComparer();

    public static FilterExpression Canonicalize(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return CanonicalizeCore(filter);
    }

    public static string Signature(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return SignatureOf(CanonicalizeCore(filter));
    }

    private static FilterExpression CanonicalizeCore(FilterExpression filter) =>
        filter.Kind switch
        {
            FilterExpressionKind.And => CanonicalizeAnd(filter),
            FilterExpressionKind.Or => CanonicalizeOr(filter),
            FilterExpressionKind.Not => CanonicalizeNot(filter),
            FilterExpressionKind.In => CanonicalizeIn(filter),
            _ => filter,
        };

    private static FilterExpression CanonicalizeAnd(FilterExpression filter)
    {
        var children = new List<FilterExpression>();
        Flatten(filter, FilterExpressionKind.And, children);

        // And(x, Any) == x: redundant Any children are dropped.
        children.RemoveAll(static child => child.Kind == FilterExpressionKind.Any);
        if (children.Count == 0)
            return FilterExpression.Any;

        return Combine(FilterExpressionKind.And, children);
    }

    private static FilterExpression CanonicalizeOr(FilterExpression filter)
    {
        var children = new List<FilterExpression>();
        Flatten(filter, FilterExpressionKind.Or, children);

        // Or(x, Any) == Any: an always-true branch collapses the whole Or.
        if (children.Count == 0 || children.Exists(static child => child.Kind == FilterExpressionKind.Any))
            return FilterExpression.Any;

        return Combine(FilterExpressionKind.Or, children);
    }

    private static FilterExpression CanonicalizeNot(FilterExpression filter)
    {
        if (filter.Children.Length != 1)
            return filter;

        return new FilterExpression(FilterExpressionKind.Not)
        {
            Children = [CanonicalizeCore(filter.Children[0])],
        };
    }

    private static FilterExpression CanonicalizeIn(FilterExpression filter)
    {
        if (filter.Values.Length <= 1)
            return filter;

        var ordered = new SortedDictionary<string, FilterValue>(StringComparer.Ordinal);
        foreach (FilterValue value in filter.Values)
            ordered.TryAdd(ValueSignature(value), value);

        return filter with { Values = ordered.Values.ToArray() };
    }

    private static void Flatten(
        FilterExpression filter,
        FilterExpressionKind kind,
        List<FilterExpression> output)
    {
        foreach (FilterExpression child in filter.Children)
        {
            FilterExpression canonical = CanonicalizeCore(child);
            if (canonical.Kind == kind)
                Flatten(canonical, kind, output);
            else
                output.Add(canonical);
        }
    }

    private static FilterExpression Combine(
        FilterExpressionKind kind,
        List<FilterExpression> children)
    {
        var ordered = new SortedDictionary<string, FilterExpression>(StringComparer.Ordinal);
        foreach (FilterExpression child in children)
            ordered.TryAdd(SignatureOf(child), child);

        if (ordered.Count == 1)
            return ordered.Values.First();

        return new FilterExpression(kind) { Children = ordered.Values.ToArray() };
    }

    private static string SignatureOf(FilterExpression filter)
    {
        var builder = new StringBuilder();
        AppendExpression(builder, filter);
        return builder.ToString();
    }

    private static void AppendExpression(StringBuilder builder, FilterExpression filter)
    {
        builder.Append('(').Append((int)filter.Kind);
        switch (filter.Kind)
        {
            case FilterExpressionKind.Compare:
                AppendField(builder, filter.Field);
                builder.Append(':').Append((int)filter.Operator);
                if (filter.IgnoreCase)
                    builder.Append('~');
                AppendValue(builder, filter.Value);
                break;
            case FilterExpressionKind.In:
                AppendField(builder, filter.Field);
                builder.Append('[').Append(filter.Values.Length).Append(']');
                foreach (FilterValue value in filter.Values)
                    AppendValue(builder, value);
                break;
            case FilterExpressionKind.Exists:
                AppendField(builder, filter.Field);
                break;
            case FilterExpressionKind.Contains:
                AppendField(builder, filter.Field);
                AppendValue(builder, filter.Value);
                break;
            case FilterExpressionKind.Count:
                AppendField(builder, filter.Field);
                builder.Append(':').Append((int)filter.Operator);
                AppendValue(builder, filter.Value);
                break;
            case FilterExpressionKind.And:
            case FilterExpressionKind.Or:
            case FilterExpressionKind.Not:
                builder.Append('[').Append(filter.Children.Length).Append(']');
                foreach (FilterExpression child in filter.Children)
                    AppendExpression(builder, child);
                break;
        }

        builder.Append(')');
    }

    private static void AppendField(StringBuilder builder, string field) =>
        builder.Append('|').Append(field.Length).Append(':').Append(field);

    private static void AppendValue(StringBuilder builder, FilterValue? value)
    {
        if (value is null)
        {
            builder.Append("_");
            return;
        }

        builder.Append('{').Append(ValueSignature(value)).Append('}');
    }

    private static string ValueSignature(FilterValue value)
    {
        string payload = value.Kind switch
        {
            FilterValueKind.Null => "n",
            FilterValueKind.Boolean => value.Boolean ? "b1" : "b0",
            FilterValueKind.Integer => "i" + value.Integer.ToString(CultureInfo.InvariantCulture),
            FilterValueKind.UnsignedInteger => "u" + value.UnsignedInteger.ToString(CultureInfo.InvariantCulture),
            FilterValueKind.Number => "d" + value.Number.ToString("R", CultureInfo.InvariantCulture),
            FilterValueKind.Decimal => "m" + value.Decimal.ToString(CultureInfo.InvariantCulture),
            FilterValueKind.String => value.String is null
                ? "s_"
                : "s" + value.String.Length.ToString(CultureInfo.InvariantCulture) + ":" + value.String,
            FilterValueKind.Guid => "g" + value.Guid.ToString("N"),
            _ => "?" + (int)value.Kind,
        };

        return value.ParameterKey is null ? payload : payload + "#" + value.ParameterKey;
    }

    private sealed class StructuralComparer : IEqualityComparer<FilterExpression>
    {
        public bool Equals(FilterExpression? x, FilterExpression? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return string.Equals(Signature(x), Signature(y), StringComparison.Ordinal);
        }

        public int GetHashCode(FilterExpression obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return Signature(obj).GetHashCode(StringComparison.Ordinal);
        }
    }
}

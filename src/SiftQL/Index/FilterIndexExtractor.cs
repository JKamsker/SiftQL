using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Index;

public static class FilterIndexExtractor
{
    private const double LongMaxExclusive = 9_223_372_036_854_775_808D;
    private const double ULongMaxExclusive = 18_446_744_073_709_551_616D;

    public static FilterIndexKey? Extract(
        Type subjectType,
        FilterExpression? expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        var schema = FilterSchema.For(subjectType);
        return Extract(schema, expression ?? FilterExpression.Any, errorFactory);
    }

    public static FilterIndexKey? Extract(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(expression);
        FilterExpressionShapeValidator.Validate(expression, errorFactory);
        return FindExactScalar(schema, expression, errorFactory);
    }

    // Extracts every index key a subscription can be registered under. Unlike
    // Extract (single key), this descends In (one key per value) and Or (one key
    // per branch, but only when every branch is itself indexable -- otherwise an
    // event matching an un-indexable branch would be missed). And selects the
    // single most selective indexable child, which may itself yield many keys.
    public static IReadOnlyList<FilterIndexKey> ExtractKeys(
        Type subjectType,
        FilterExpression? expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        var schema = FilterSchema.For(subjectType);
        return ExtractKeys(schema, expression ?? FilterExpression.Any, errorFactory);
    }

    public static IReadOnlyList<FilterIndexKey> ExtractKeys(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(expression);
        FilterExpressionShapeValidator.Validate(expression, errorFactory);
        return CollectKeys(schema, expression, errorFactory);
    }

    private static IReadOnlyList<FilterIndexKey> CollectKeys(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory) =>
        expression.Kind switch
        {
            FilterExpressionKind.Compare => Single(BuildCompareKey(schema, expression, errorFactory)),
            FilterExpressionKind.In => CollectInKeys(schema, expression, errorFactory),
            FilterExpressionKind.Or => CollectOrKeys(schema, expression, errorFactory),
            FilterExpressionKind.And => CollectAndKeys(schema, expression, errorFactory),
            _ => [],
        };

    private static IReadOnlyList<FilterIndexKey> Single(FilterIndexKey? key) =>
        key is null ? [] : [key];

    private static IReadOnlyList<FilterIndexKey> CollectInKeys(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Values.Length == 0 ||
            !schema.TryGetField(expression.Field, out FilterField? field) ||
            field.Kind != FilterFieldKind.Scalar)
        {
            return [];
        }

        var keys = new List<FilterIndexKey>(expression.Values.Length);
        var seen = new HashSet<FilterIndexValue>();
        foreach (FilterValue value in expression.Values)
        {
            FilterValues.ValidateValue(field, value, errorFactory);

            // A null or otherwise unindexable value means the whole In cannot be
            // fully indexed; falling back to one scan is safer than missing it.
            if (value.Kind == FilterValueKind.Null ||
                !TryCreateFieldValue(field, value, out FilterIndexValue indexValue))
            {
                return [];
            }

            if (seen.Add(indexValue))
                keys.Add(new FilterIndexKey(field.Name, indexValue));
        }

        return keys;
    }

    private static IReadOnlyList<FilterIndexKey> CollectOrKeys(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Children.Length == 0)
            return [];

        var keys = new List<FilterIndexKey>();
        var seen = new HashSet<FilterIndexKey>();
        foreach (FilterExpression child in expression.Children)
        {
            IReadOnlyList<FilterIndexKey> childKeys = CollectKeys(schema, child, errorFactory);
            if (childKeys.Count == 0)
                return [];

            foreach (FilterIndexKey key in childKeys)
            {
                if (seen.Add(key))
                    keys.Add(key);
            }
        }

        return keys;
    }

    private static IReadOnlyList<FilterIndexKey> CollectAndKeys(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        IReadOnlyList<FilterIndexKey> best = [];
        int bestScore = int.MaxValue;
        foreach (FilterExpression child in expression.Children)
        {
            IReadOnlyList<FilterIndexKey> childKeys = CollectKeys(schema, child, errorFactory);
            if (childKeys.Count == 0)
                continue;

            int score = ScoreKeys(childKeys);
            if (score < bestScore)
            {
                best = childKeys;
                bestScore = score;
            }
        }

        return best;
    }

    private static int ScoreKeys(IReadOnlyList<FilterIndexKey> keys)
    {
        int score = 0;
        for (int i = 0; i < keys.Count; i++)
            score = Math.Max(score, SelectivityScore(keys[i].Field));
        return score;
    }

    // Extracts the most selective range condition (Between, an ordered Compare, or
    // a merged And of them) on a single range-indexable field. Used only when a
    // subscription has no equality key, to accelerate threshold/range filters that
    // would otherwise full-scan.
    internal static RangeCondition? ExtractRange(FilterSchema schema, FilterExpression expression)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(expression);
        return CollectRange(schema, expression);
    }

    private static RangeCondition? CollectRange(FilterSchema schema, FilterExpression expression) =>
        expression.Kind switch
        {
            FilterExpressionKind.Between => BuildBetweenRange(schema, expression),
            FilterExpressionKind.Compare => BuildCompareRange(schema, expression),
            FilterExpressionKind.And => CollectAndRange(schema, expression),
            _ => null,
        };

    private static RangeCondition? BuildBetweenRange(FilterSchema schema, FilterExpression expression)
    {
        if (!IsRangeField(schema, expression.Field) || expression.Values.Length != 2)
            return null;

        decimal? lower = RangeKey.TryFromValue(expression.Values[0], out decimal lo) ? lo : null;
        decimal? upper = RangeKey.TryFromValue(expression.Values[1], out decimal hi) ? hi : null;
        // Reversed bounds describe an empty interval; never build an inverted range
        // condition. The full predicate (CompileBetween) rejects these at compile
        // time, so leaving them unindexed here keeps the index consistent.
        if (lower.HasValue && upper.HasValue && lower > upper)
            return null;
        return lower.HasValue || upper.HasValue ? new RangeCondition(expression.Field, lower, upper) : null;
    }

    private static RangeCondition? BuildCompareRange(FilterSchema schema, FilterExpression expression)
    {
        if (expression.Value is null || !IsRangeField(schema, expression.Field))
            return null;
        if (!RangeKey.TryFromValue(expression.Value, out decimal key))
            return null;

        return expression.Operator switch
        {
            FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual =>
                new RangeCondition(expression.Field, key, null),
            FilterOperator.LessThan or FilterOperator.LessThanOrEqual =>
                new RangeCondition(expression.Field, null, key),
            _ => null,
        };
    }

    private static RangeCondition? CollectAndRange(FilterSchema schema, FilterExpression expression)
    {
        var byField = new Dictionary<string, RangeCondition>(StringComparer.OrdinalIgnoreCase);
        foreach (FilterExpression child in expression.Children)
        {
            if (CollectRange(schema, child) is not { } condition)
                continue;

            byField[condition.Field] = byField.TryGetValue(condition.Field, out RangeCondition existing)
                ? Merge(existing, condition)
                : condition;
        }

        RangeCondition? best = null;
        int bestScore = int.MaxValue;
        foreach (RangeCondition condition in byField.Values)
        {
            // Prefer bounded conditions; among equal boundedness, prefer the more
            // selective field.
            int score = SelectivityScore(condition.Field) - (condition.IsBounded ? 1000 : 0);
            if (score < bestScore)
            {
                best = condition;
                bestScore = score;
            }
        }

        return best;
    }

    private static RangeCondition Merge(RangeCondition a, RangeCondition b) =>
        new(
            a.Field,
            MaxBound(a.Lower, b.Lower),
            MinBound(a.Upper, b.Upper));

    private static decimal? MaxBound(decimal? x, decimal? y) =>
        x is null ? y : y is null ? x : Math.Max(x.Value, y.Value);

    private static decimal? MinBound(decimal? x, decimal? y) =>
        x is null ? y : y is null ? x : Math.Min(x.Value, y.Value);

    private static bool IsRangeField(FilterSchema schema, string field) =>
        schema.TryGetField(field, out FilterField? resolved) &&
        resolved.Kind == FilterFieldKind.Scalar &&
        RangeKey.IsIndexableField(resolved.ValueType);

    private static FilterIndexKey? FindExactScalar(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Kind == FilterExpressionKind.Compare)
            return BuildCompareKey(schema, expression, errorFactory);

        if (expression.Kind == FilterExpressionKind.And)
        {
            FilterIndexKey? best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < expression.Children.Length; i++)
            {
                FilterIndexKey? key = FindExactScalar(schema, expression.Children[i], errorFactory);
                if (key is null)
                    continue;

                int score = SelectivityScore(key.Field);
                if (score < bestScore)
                {
                    best = key;
                    bestScore = score;
                }
            }

            return best;
        }

        return null;
    }

    private static int SelectivityScore(string field) =>
        field switch
        {
            "subjectType" or "subjectName" => 100,
            _ when field.EndsWith("Id", StringComparison.Ordinal) ||
                field.EndsWith(".Id", StringComparison.Ordinal) => 0,
            _ when field.Contains("Character", StringComparison.Ordinal) ||
                field.Contains("Session", StringComparison.Ordinal) ||
                field.Contains("Item", StringComparison.Ordinal) ||
                field.Contains("Skill", StringComparison.Ordinal) => 5,
            _ => 50,
        };

    private static FilterIndexKey? BuildCompareKey(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Value is null)
            return null;
        if (!schema.TryGetField(expression.Field, out FilterField? field) ||
            field.Kind != FilterFieldKind.Scalar)
        {
            return null;
        }

        FilterValues.ValidateComparison(field, expression.Operator, expression.Value, errorFactory, expression.IgnoreCase);
        if (expression.Operator != FilterOperator.Equal)
            return null;

        // Case-insensitive equality cannot use the ordinal-keyed buckets; leave it
        // unindexed (matching stays correct via the full predicate).
        if (expression.IgnoreCase)
            return null;

        return TryCreateFieldValue(field, expression.Value, out FilterIndexValue value)
            ? new FilterIndexKey(field.Name, value)
            : null;
    }

    private static bool TryCreateFieldValue(
        FilterField field,
        FilterValue value,
        out FilterIndexValue key)
    {
        key = default;
        if (field.ValueType == typeof(ProjectedEventValue))
            return false;

        Type type = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        if (type == typeof(decimal) || value.Kind == FilterValueKind.Decimal)
            return false;

        if (type.IsEnum)
            return TryCreateEnumValue(type, value, out key);

        if (type == typeof(ulong) && value.Kind == FilterValueKind.Integer)
        {
            if (value.Integer < 0)
                return false;
            key = FilterIndexValue.ForInteger(value.Integer);
            return true;
        }

        if (value.Kind == FilterValueKind.UnsignedInteger &&
            (FilterNumeric.IsUnsignedIntegral(type) || FilterNumeric.IsSignedIntegral(type)))
        {
            if (value.UnsignedInteger <= long.MaxValue)
            {
                key = FilterIndexValue.ForInteger((long)value.UnsignedInteger);
                return true;
            }

            if (type == typeof(ulong))
            {
                key = FilterIndexValue.ForUnsignedInteger(value.UnsignedInteger);
                return true;
            }

            return false;
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.Integer)
        {
            return TryCreateFloatingIntegerValue(value.Integer, out key);
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.UnsignedInteger)
        {
            return TryCreateFloatingUnsignedIntegerValue(value.UnsignedInteger, out key);
        }

        if (value.Kind == FilterValueKind.Number &&
            (FilterNumeric.IsSignedIntegral(type) || FilterNumeric.IsUnsignedIntegral(type)))
        {
            if (!FilterNumeric.TryDoubleToInt64(value.Number, out long integer) ||
                (FilterNumeric.IsUnsignedIntegral(type) && integer < 0))
            {
                return false;
            }

            key = FilterIndexValue.ForInteger(integer);
            return true;
        }

        return FilterIndexValue.TryCreate(value, out key);
    }

    private static bool TryCreateEnumValue(
        Type enumType,
        FilterValue value,
        out FilterIndexValue key)
    {
        key = default;
        if (value.Kind == FilterValueKind.Integer)
        {
            if (Enum.GetUnderlyingType(enumType) == typeof(ulong) && value.Integer < 0)
                return false;

            key = FilterIndexValue.ForEnum(value.Integer);
            return true;
        }

        if (value.Kind != FilterValueKind.String ||
            string.IsNullOrWhiteSpace(value.String) ||
            !Enum.TryParse(enumType, value.String, ignoreCase: false, out object? parsed))
        {
            return false;
        }

        if (!TryConvertEnumToInt64(parsed, out long enumValue))
            return false;

        key = FilterIndexValue.ForEnum(enumValue);
        return true;
    }

    private static bool TryConvertEnumToInt64(object value, out long result)
    {
        try
        {
            result = Convert.ToInt64(value);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static bool IsFloating(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool TryCreateFloatingIntegerValue(
        long value,
        out FilterIndexValue key)
    {
        key = default;
        double number = value;
        if (number >= LongMaxExclusive || (long)number != value)
            return false;

        key = FilterIndexValue.ForNumber(number);
        return true;
    }

    private static bool TryCreateFloatingUnsignedIntegerValue(
        ulong value,
        out FilterIndexValue key)
    {
        key = default;
        double number = value;
        if (number >= ULongMaxExclusive || (ulong)number != value)
            return false;

        key = FilterIndexValue.ForNumber(number);
        return true;
    }
}

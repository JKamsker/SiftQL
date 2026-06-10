using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Index;

// A range condition reduces to a closed-ish interval on a single decimal key.
// decimal is exact for integral, decimal, and temporal (UTC ticks) fields, which
// is why only those field types are range-indexed; double/float fields stay
// unindexed (sound -- matched by full evaluation) to avoid precision/infinity
// pitfalls. Inclusivity is intentionally dropped: the index is candidate-only and
// the full predicate re-checks, so treating bounds as inclusive can only produce
// (harmless) false positives, never false negatives.
internal readonly record struct RangeCondition(string Field, decimal? Lower, decimal? Upper)
{
    public bool IsBounded => Lower.HasValue && Upper.HasValue;
    public bool HasAnyBound => Lower.HasValue || Upper.HasValue;
}

internal static class RangeKey
{
    private const decimal DecimalRangeLimit = 7.9e28m;

    public static bool IsIndexableField(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) ||
            type == typeof(decimal) ||
            type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly);
    }

    public static Func<object, decimal?> CreateAccessor(FilterField field)
    {
        Func<object, object?> getter = field.Getter;
        return subject => FromActual(getter(subject));
    }

    public static bool TryFromValue(FilterValue value, out decimal key)
    {
        switch (value.Kind)
        {
            case FilterValueKind.Integer:
                key = value.Integer;
                return true;
            case FilterValueKind.UnsignedInteger:
                key = value.UnsignedInteger;
                return true;
            case FilterValueKind.Decimal:
                key = value.Decimal;
                return true;
            case FilterValueKind.Timestamp:
                key = value.Timestamp.UtcTicks;
                return true;
            case FilterValueKind.Number when double.IsFinite(value.Number) && Math.Abs(value.Number) < (double)DecimalRangeLimit:
                key = (decimal)value.Number;
                return true;
            default:
                key = 0;
                return false;
        }
    }

    private static decimal? FromActual(object? value) =>
        value switch
        {
            null => null,
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            decimal v => v,
            DateTimeOffset v => v.UtcTicks,
            DateTime v => ToTimestamp(v).UtcTicks,
            DateOnly v => new DateTimeOffset(v.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).UtcTicks,
            _ => null,
        };

    private static DateTimeOffset ToTimestamp(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
        };
}

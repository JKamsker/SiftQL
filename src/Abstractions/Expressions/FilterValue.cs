using System.Text.Json.Serialization;
using SiftQL.Translation;

namespace SiftQL.Expressions;

public enum FilterValueKind
{
    Null = 0,
    Boolean = 1,
    Integer = 2,
    Number = 3,
    String = 4,
    Guid = 5,
    UnsignedInteger = 6,
    Decimal = 7,
    Timestamp = 8,
}

public sealed record FilterValue
{
    public FilterValueKind Kind { get; init; }
    public string? ParameterKey { get; init; }
    public bool Boolean { get; init; }
    public long Integer { get; init; }
    public ulong UnsignedInteger { get; init; }

    // NaN and +/-Infinity are valid filter values but have no JSON number
    // representation; serialize them as the named literals instead.
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public double Number { get; init; }
    public decimal Decimal { get; init; }
    public string? String { get; init; }
    public Guid Guid { get; init; }

    // Temporal point-in-time (DateTime/DateTimeOffset/DateOnly), compared by
    // instant. Serialized only when set so non-temporal values keep their wire
    // format and fingerprints.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset Timestamp { get; init; }

    public static FilterValue Null { get; } = new() { Kind = FilterValueKind.Null };

    public static FilterValue From(bool value) =>
        new() { Kind = FilterValueKind.Boolean, Boolean = value };

    public static FilterValue From(long value) =>
        new() { Kind = FilterValueKind.Integer, Integer = value };

    public static FilterValue From(ulong value) =>
        value <= long.MaxValue
            ? From((long)value)
            : new() { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = value };

    public static FilterValue From(double value) =>
        new() { Kind = FilterValueKind.Number, Number = value };

    public static FilterValue From(decimal value) =>
        IsIntegralInt64(value)
            ? From((long)value)
            : new() { Kind = FilterValueKind.Decimal, Decimal = value };

    public static FilterValue From(string value) =>
        new() { Kind = FilterValueKind.String, String = value };

    public static FilterValue From(Guid value) =>
        new() { Kind = FilterValueKind.Guid, Guid = value };

    public static FilterValue From(DateTimeOffset value) =>
        new() { Kind = FilterValueKind.Timestamp, Timestamp = value };

    public static FilterValue From(DateTime value) =>
        From(ToTimestamp(value));

    public static FilterValue From(DateOnly value) =>
        From(new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static DateTimeOffset ToTimestamp(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            // Unspecified is assumed UTC for deterministic, machine-independent results.
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
        };

    public static FilterValue FromObject(object? value)
    {
        if (value is null)
        {
            return Null;
        }

        Type type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (type.IsEnum)
        {
            return From(value.ToString() ?? string.Empty);
        }

        return value switch
        {
            bool item => From(item),
            byte item => From(item),
            sbyte item => From(item),
            short item => From(item),
            ushort item => From(item),
            int item => From(item),
            uint item => From(item),
            long item => From(item),
            ulong item => From(item),
            float item => From(item),
            double item => From(item),
            decimal item => From(item),
            string item => From(item),
            Guid item => From(item),
            DateTimeOffset item => From(item),
            DateTime item => From(item),
            DateOnly item => From(item),
            _ => throw new KernelExpressionException(
                $"Filter constants of type '{type.FullName}' are not supported."),
        };
    }

    private static bool IsIntegralInt64(decimal value) =>
        decimal.Truncate(value) == value &&
        value >= long.MinValue &&
        value <= long.MaxValue;
}

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
}

public sealed record FilterValue
{
    public FilterValueKind Kind { get; init; }
    public string? ParameterKey { get; init; }
    public bool Boolean { get; init; }
    public long Integer { get; init; }
    public ulong UnsignedInteger { get; init; }
    public double Number { get; init; }
    public string? String { get; init; }
    public Guid Guid { get; init; }

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

    public static FilterValue From(string value) =>
        new() { Kind = FilterValueKind.String, String = value };

    public static FilterValue From(Guid value) =>
        new() { Kind = FilterValueKind.Guid, Guid = value };

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
            decimal item when IsIntegralInt64(item) => From((long)item),
            decimal item => From((double)item),
            string item => From(item),
            Guid item => From(item),
            _ => throw new KernelExpressionException(
                $"Filter constants of type '{type.FullName}' are not supported."),
        };
    }

    private static bool IsIntegralInt64(decimal value) =>
        decimal.Truncate(value) == value &&
        value >= long.MinValue &&
        value <= long.MaxValue;
}

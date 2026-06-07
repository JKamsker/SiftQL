using System.Collections;

using SiftQL.Translation;

namespace SiftQL.Projected;

public enum ProjectedEventValueKind
{
    Null = 0,
    Boolean = 1,
    Integer = 2,
    Number = 3,
    String = 4,
    Guid = 5,
    Array = 6,
    Object = 7,
    UnsignedInteger = 8,
}

public sealed record ProjectedEventValue
{
    private const int MaxArrayItems = 256;

    public ProjectedEventValueKind Kind { get; init; }
    public bool Boolean { get; init; }
    public long Integer { get; init; }
    public ulong UnsignedInteger { get; init; }
    public double Number { get; init; }
    public string? String { get; init; }
    public Guid Guid { get; init; }
    public ProjectedEventValue[] Values { get; init; } = [];
    public ProjectedEventField[] Fields { get; init; } = [];

    public static ProjectedEventValue Null { get; } = new();

    public static ProjectedEventValue FromScalar(object? value)
    {
        if (value is null)
            return Null;

        Type type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (type.IsEnum)
            return FromString(value.ToString() ?? string.Empty);

        return value switch
        {
            bool item => new() { Kind = ProjectedEventValueKind.Boolean, Boolean = item },
            byte item => FromInteger(item),
            sbyte item => FromInteger(item),
            short item => FromInteger(item),
            ushort item => FromInteger(item),
            int item => FromInteger(item),
            uint item => FromInteger(item),
            long item => FromInteger(item),
            ulong item => FromUInt64(item),
            float item => FromNumber(item),
            double item => FromNumber(item),
            decimal item => FromDecimal(item),
            string item => FromString(item),
            Guid item => new() { Kind = ProjectedEventValueKind.Guid, Guid = item },
            IEnumerable items when value is not string => FromArray(items),
            _ => throw new KernelExpressionException(
                $"Projected value type '{type.FullName}' is not supported."),
        };
    }

    public static ProjectedEventValue FromArray(IEnumerable items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var values = new List<ProjectedEventValue>();
        foreach (object? item in items)
        {
            if (values.Count >= MaxArrayItems)
            {
                throw new KernelExpressionException(
                    $"Projected arrays cannot exceed {MaxArrayItems} items.");
            }

            values.Add(FromScalar(item));
        }

        return new()
        {
            Kind = ProjectedEventValueKind.Array,
            Values = values.ToArray(),
        };
    }

    public static ProjectedEventValue FromValues(IEnumerable<ProjectedEventValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new() { Kind = ProjectedEventValueKind.Array, Values = values.ToArray() };
    }

    public static ProjectedEventValue FromFields(IEnumerable<ProjectedEventField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new() { Kind = ProjectedEventValueKind.Object, Fields = fields.ToArray() };
    }

    public static ProjectedEventValue FromDecimal(decimal value) =>
        IsIntegralInt64(value) ? FromInteger((long)value) : FromNumber((double)value);

    public static ProjectedEventValue FromUInt64(ulong value) =>
        value <= long.MaxValue ? FromInteger((long)value) : FromUnsignedInteger(value);

    private static ProjectedEventValue FromInteger(long value) =>
        new() { Kind = ProjectedEventValueKind.Integer, Integer = value };

    private static ProjectedEventValue FromUnsignedInteger(ulong value) =>
        new() { Kind = ProjectedEventValueKind.UnsignedInteger, UnsignedInteger = value };

    private static ProjectedEventValue FromNumber(double value) =>
        new() { Kind = ProjectedEventValueKind.Number, Number = value };

    private static ProjectedEventValue FromString(string value) =>
        new() { Kind = ProjectedEventValueKind.String, String = value };

    private static bool IsIntegralInt64(decimal value) =>
        decimal.Truncate(value) == value &&
        value >= long.MinValue &&
        value <= long.MaxValue;
}

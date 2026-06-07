using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

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
    Decimal = 9,
}

public sealed record ProjectedEventValue
{
    private const int MaxArrayItems = 256;
    private const int MaxObjectDepth = 6;
    private const int MaxObjectFields = 64;
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_objectProperties = new();

    public ProjectedEventValueKind Kind { get; init; }
    public bool Boolean { get; init; }
    public long Integer { get; init; }
    public ulong UnsignedInteger { get; init; }
    public double Number { get; init; }
    public decimal Decimal { get; init; }
    public string? String { get; init; }
    public Guid Guid { get; init; }
    public ProjectedEventValue[] Values { get; init; } = [];
    public ProjectedEventField[] Fields { get; init; } = [];

    public static ProjectedEventValue Null { get; } = new();

    public static ProjectedEventValue FromScalar(object? value)
    {
        if (value is null)
            return Null;

        return FromValue(value, depth: 0);
    }

    public static ProjectedEventValue FromObject(object? value) =>
        value is null ? Null : FromObjectValue(value, depth: 0);

    private static ProjectedEventValue FromValue(object value, int depth)
    {
        if (depth > MaxObjectDepth)
        {
            throw new KernelExpressionException(
                $"Projected objects cannot exceed {MaxObjectDepth} nested levels.");
        }

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
            IEnumerable items when value is not string => FromArray(items, depth + 1),
            _ => FromObjectValue(value, depth + 1),
        };
    }

    public static ProjectedEventValue FromArray(IEnumerable items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return FromArray(items, depth: 0);
    }

    private static ProjectedEventValue FromArray(IEnumerable items, int depth)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items is ICollection collection)
            return FromCollection(collection, depth);

        var values = new List<ProjectedEventValue>();
        foreach (object? item in items)
        {
            if (values.Count >= MaxArrayItems)
            {
                throw new KernelExpressionException(
                    $"Projected arrays cannot exceed {MaxArrayItems} items.");
            }

            values.Add(item is null ? Null : FromValue(item, depth));
        }

        return new()
        {
            Kind = ProjectedEventValueKind.Array,
            Values = values.ToArray(),
        };
    }

    private static ProjectedEventValue FromCollection(ICollection items, int depth)
    {
        if (items.Count > MaxArrayItems)
        {
            throw new KernelExpressionException(
                $"Projected arrays cannot exceed {MaxArrayItems} items.");
        }

        var values = new ProjectedEventValue[items.Count];
        int index = 0;
        foreach (object? item in items)
            values[index++] = item is null ? Null : FromValue(item, depth);

        return new()
        {
            Kind = ProjectedEventValueKind.Array,
            Values = values,
        };
    }

    private static ProjectedEventValue FromObjectValue(object value, int depth)
    {
        PropertyInfo[] properties = s_objectProperties.GetOrAdd(value.GetType(), DiscoverObjectProperties);
        if (properties.Length > MaxObjectFields)
        {
            throw new KernelExpressionException(
                $"Projected objects cannot exceed {MaxObjectFields} fields.");
        }

        var fields = new ProjectedEventField[properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            object? item = property.GetValue(value);
            fields[i] = new ProjectedEventField(
                property.Name,
                item is null ? Null : FromValue(item, depth + 1));
        }

        return new() { Kind = ProjectedEventValueKind.Object, Fields = fields };
    }

    private static PropertyInfo[] DiscoverObjectProperties(Type type) =>
        FilterObjectProperties(type.GetProperties(BindingFlags.Instance | BindingFlags.Public));

    private static PropertyInfo[] FilterObjectProperties(PropertyInfo[] properties)
    {
        int count = 0;
        for (int i = 0; i < properties.Length; i++)
        {
            if (IsProjectableProperty(properties[i]))
                count++;
        }

        if (count == properties.Length)
            return properties;

        var filtered = new PropertyInfo[count];
        int index = 0;
        for (int i = 0; i < properties.Length; i++)
        {
            if (IsProjectableProperty(properties[i]))
                filtered[index++] = properties[i];
        }

        return filtered;
    }

    private static bool IsProjectableProperty(PropertyInfo property) =>
        property.GetMethod is not null &&
        property.GetMethod.GetParameters().Length == 0;

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
        IsIntegralInt64(value) ? FromInteger((long)value) : FromExactDecimal(value);

    public static ProjectedEventValue FromUInt64(ulong value) =>
        value <= long.MaxValue ? FromInteger((long)value) : FromUnsignedInteger(value);

    private static ProjectedEventValue FromInteger(long value) =>
        new() { Kind = ProjectedEventValueKind.Integer, Integer = value };

    private static ProjectedEventValue FromUnsignedInteger(ulong value) =>
        new() { Kind = ProjectedEventValueKind.UnsignedInteger, UnsignedInteger = value };

    private static ProjectedEventValue FromNumber(double value) =>
        new() { Kind = ProjectedEventValueKind.Number, Number = value };

    private static ProjectedEventValue FromExactDecimal(decimal value) =>
        new() { Kind = ProjectedEventValueKind.Decimal, Decimal = value };

    private static ProjectedEventValue FromString(string value) =>
        new() { Kind = ProjectedEventValueKind.String, String = value };

    private static bool IsIntegralInt64(decimal value) =>
        decimal.Truncate(value) == value &&
        value >= long.MinValue &&
        value <= long.MaxValue;
}

using SiftQL;
using SiftQL.Projected;

namespace SiftQL;

public static class ProjectionValueFactory
{
    public static ProjectedEventValue FromBoolean(bool value) =>
        new() { Kind = ProjectedEventValueKind.Boolean, Boolean = value };

    public static ProjectedEventValue FromBoolean(bool? value) =>
        value.HasValue ? FromBoolean(value.Value) : ProjectedEventValue.Null;

    public static ProjectedEventValue FromByte(byte value) => FromInteger(value);
    public static ProjectedEventValue FromSByte(sbyte value) => FromInteger(value);
    public static ProjectedEventValue FromInt16(short value) => FromInteger(value);
    public static ProjectedEventValue FromUInt16(ushort value) => FromInteger(value);
    public static ProjectedEventValue FromInt32(int value) => FromInteger(value);
    public static ProjectedEventValue FromUInt32(uint value) => FromInteger(value);
    public static ProjectedEventValue FromInt64(long value) => FromInteger(value);

    public static ProjectedEventValue FromByte(byte? value) => FromInteger(value);
    public static ProjectedEventValue FromSByte(sbyte? value) => FromInteger(value);
    public static ProjectedEventValue FromInt16(short? value) => FromInteger(value);
    public static ProjectedEventValue FromUInt16(ushort? value) => FromInteger(value);
    public static ProjectedEventValue FromInt32(int? value) => FromInteger(value);
    public static ProjectedEventValue FromUInt32(uint? value) => FromInteger(value);
    public static ProjectedEventValue FromInt64(long? value) => FromInteger(value);

    public static ProjectedEventValue FromUInt64(ulong value) =>
        ProjectedEventValue.FromUInt64(value);

    public static ProjectedEventValue FromUInt64(ulong? value)
    {
        if (!value.HasValue)
            return ProjectedEventValue.Null;

        return FromUInt64(value.Value);
    }

    public static ProjectedEventValue FromSingle(float value) => FromNumber(value);

    public static ProjectedEventValue FromSingle(float? value) =>
        value.HasValue ? FromNumber(value.Value) : ProjectedEventValue.Null;

    public static ProjectedEventValue FromDouble(double value) => FromNumber(value);

    public static ProjectedEventValue FromDouble(double? value) =>
        value.HasValue ? FromNumber(value.Value) : ProjectedEventValue.Null;

    public static ProjectedEventValue FromDecimal(decimal value) =>
        ProjectedEventValue.FromDecimal(value);

    public static ProjectedEventValue FromDecimal(decimal? value) =>
        value.HasValue ? ProjectedEventValue.FromDecimal(value.Value) : ProjectedEventValue.Null;

    public static ProjectedEventValue FromString(string? value) =>
        value is null
            ? ProjectedEventValue.Null
            : new ProjectedEventValue { Kind = ProjectedEventValueKind.String, String = value };

    public static ProjectedEventValue FromGuid(Guid value) =>
        new() { Kind = ProjectedEventValueKind.Guid, Guid = value };

    public static ProjectedEventValue FromGuid(Guid? value) =>
        value.HasValue ? FromGuid(value.Value) : ProjectedEventValue.Null;

    public static ProjectedEventValue FromEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        FromString(value.ToString());

    public static ProjectedEventValue FromEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue ? FromString(value.Value.ToString()) : ProjectedEventValue.Null;

    private static ProjectedEventValue FromInteger(long? value) =>
        value.HasValue ? FromInteger(value.Value) : ProjectedEventValue.Null;

    private static ProjectedEventValue FromInteger(long value) =>
        new() { Kind = ProjectedEventValueKind.Integer, Integer = value };

    private static ProjectedEventValue FromNumber(double value) =>
        new() { Kind = ProjectedEventValueKind.Number, Number = value };
}

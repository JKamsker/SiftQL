using SiftQL;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Index;

public readonly record struct FilterIndexValue(
    FilterValueKind Kind,
    bool Boolean,
    long Integer,
    ulong UnsignedInteger,
    double Number,
    string? String,
    Guid Guid)
{
    internal static FilterIndexValue ForBoolean(bool value) =>
        new(FilterValueKind.Boolean, value, 0, 0, 0, null, default);

    internal static FilterIndexValue? ForBoolean(bool? value) =>
        value.HasValue ? ForBoolean(value.Value) : null;

    internal static FilterIndexValue ForNumber(double value) =>
        ToNumber(value);

    internal static FilterIndexValue? ForNumber(double? value) =>
        value.HasValue ? ToNumber(value.Value) : null;

    internal static FilterIndexValue ForInteger(long value) =>
        ToInteger(value);

    internal static FilterIndexValue? ForInteger(long? value) =>
        value.HasValue ? ToInteger(value.Value) : null;

    internal static FilterIndexValue ForUnsignedInteger(ulong value) =>
        ToUnsignedInteger(value);

    internal static FilterIndexValue? ForString(string? value) =>
        value is null ? null : new(FilterValueKind.String, false, 0, 0, 0, value, default);

    internal static FilterIndexValue ForGuid(Guid value) =>
        new(FilterValueKind.Guid, false, 0, 0, 0, null, value);

    internal static FilterIndexValue? ForGuid(Guid? value) =>
        value.HasValue ? ForGuid(value.Value) : null;

    internal static FilterIndexValue ForEnum(long value) =>
        ToInteger(value);

    internal static FilterIndexValue? ForEnum(long? value) =>
        value.HasValue ? ToInteger(value.Value) : null;

    public static bool TryCreate(FilterValue value, out FilterIndexValue key)
    {
        key = default;
        switch (value.Kind)
        {
            case FilterValueKind.Boolean:
                key = new(value.Kind, value.Boolean, 0, 0, 0, null, default);
                return true;
            case FilterValueKind.Integer:
                key = ToInteger(value.Integer);
                return true;
            case FilterValueKind.UnsignedInteger:
                key = ToUnsignedInteger(value.UnsignedInteger);
                return true;
            case FilterValueKind.Number:
                key = new(value.Kind, false, 0, 0, value.Number, null, default);
                return true;
            case FilterValueKind.String:
                key = new(value.Kind, false, 0, 0, 0, value.String, default);
                return true;
            case FilterValueKind.Guid:
                key = new(value.Kind, false, 0, 0, 0, null, value.Guid);
                return true;
            default:
                return false;
        }
    }

    public static bool TryCreateActual(object? value, out FilterIndexValue key)
    {
        key = default;
        if (value?.GetType().IsEnum == true)
        {
            key = ToInteger(Convert.ToInt64(value));
            return true;
        }

        switch (value)
        {
            case bool item:
                key = new(FilterValueKind.Boolean, item, 0, 0, 0, null, default);
                return true;
            case byte item:
                key = ToInteger(item);
                return true;
            case sbyte item:
                key = ToInteger(item);
                return true;
            case short item:
                key = ToInteger(item);
                return true;
            case ushort item:
                key = ToInteger(item);
                return true;
            case int item:
                key = ToInteger(item);
                return true;
            case uint item:
                key = ToInteger(item);
                return true;
            case long item:
                key = ToInteger(item);
                return true;
            case ulong item:
                if (item <= long.MaxValue)
                {
                    key = ToInteger((long)item);
                    return true;
                }

                key = ToUnsignedInteger(item);
                return true;
            case float item:
                key = ToNumber(item);
                return true;
            case double item:
                key = ToNumber(item);
                return true;
            case decimal item:
                key = ToNumber((double)item);
                return true;
            case string item:
                key = new(FilterValueKind.String, false, 0, 0, 0, item, default);
                return true;
            case Guid item:
                key = new(FilterValueKind.Guid, false, 0, 0, 0, null, item);
                return true;
            default:
                return false;
        }
    }

    public static bool TryCreateActual(
        FilterScalarAccessor accessor,
        object subject,
        out FilterIndexValue key)
    {
        key = default;
        switch (accessor.Kind)
        {
            case FilterScalarKind.Boolean:
                bool? boolean = accessor.RequiredBoolean is { } requiredBoolean
                    ? requiredBoolean(subject)
                    : accessor.Boolean?.Invoke(subject);
                if (!boolean.HasValue) return false;
                key = new(FilterValueKind.Boolean, boolean.Value, 0, 0, 0, null, default);
                return true;
            case FilterScalarKind.Number:
                double? number = accessor.RequiredNumber is { } requiredNumber
                    ? requiredNumber(subject)
                    : accessor.Number?.Invoke(subject);
                if (!number.HasValue) return false;
                key = ToNumber(number.Value);
                return true;
            case FilterScalarKind.String:
                string? text = accessor.Text?.Invoke(subject);
                if (text is null) return false;
                key = new(FilterValueKind.String, false, 0, 0, 0, text, default);
                return true;
            case FilterScalarKind.Guid:
                Guid? guid = accessor.RequiredGuid is { } requiredGuid
                    ? requiredGuid(subject)
                    : accessor.Guid?.Invoke(subject);
                if (!guid.HasValue) return false;
                key = new(FilterValueKind.Guid, false, 0, 0, 0, null, guid.Value);
                return true;
            case FilterScalarKind.Enum:
                long? value = accessor.RequiredEnumeration is { } requiredEnumeration
                    ? requiredEnumeration(subject)
                    : accessor.Enumeration?.Invoke(subject);
                if (!value.HasValue) return false;
                key = ToInteger(value.Value);
                return true;
            default:
                return false;
        }
    }

    private static FilterIndexValue ToNumber(double value) =>
        new(FilterValueKind.Number, false, 0, 0, value, null, default);

    private static FilterIndexValue ToInteger(long value) =>
        new(FilterValueKind.Integer, false, value, 0, 0, null, default);

    private static FilterIndexValue ToUnsignedInteger(ulong value) =>
        new(FilterValueKind.UnsignedInteger, false, 0, value, 0, null, default);
}

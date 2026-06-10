using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace SiftQL.Generators.Hot;

internal static partial class HotManifestParser
{
    private static HotFilterValue? ParseValue(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        if (!TryReadEnum(element, "Kind", out HotFilterValueKind kind))
        {
            Add(diagnostics, "FSFHOT009", path, "Hot filter value kind is missing or invalid.");
            return null;
        }

        bool boolean = false;
        long integer = 0;
        ulong unsignedInteger = 0;
        double number = 0;
        decimal exactDecimal = 0;
        string? text = null;
        string guid = "00000000-0000-0000-0000-000000000000";
        long timestampTicks = 0;
        string timestampText = string.Empty;
        if (!TryReadValuePayload(
            element,
            kind,
            path,
            diagnostics,
            ref boolean,
            ref integer,
            ref unsignedInteger,
            ref number,
            ref exactDecimal,
            ref text,
            ref guid,
            ref timestampTicks,
            ref timestampText))
        {
            return null;
        }

        return new(
            kind,
            ReadNullableString(element, "ParameterKey"),
            boolean,
            integer,
            unsignedInteger,
            number,
            exactDecimal,
            text,
            guid,
            timestampTicks,
            timestampText);
    }

    private static bool TryReadValuePayload(
        JsonElement element,
        HotFilterValueKind kind,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        ref bool boolean,
        ref long integer,
        ref ulong unsignedInteger,
        ref double number,
        ref decimal exactDecimal,
        ref string? text,
        ref string guid,
        ref long timestampTicks,
        ref string timestampText)
    {
        switch (kind)
        {
            case HotFilterValueKind.Null:
                return true;
            case HotFilterValueKind.Boolean:
                if (TryReadBoolean(element, "Boolean", out boolean))
                    return true;
                return Invalid("Hot filter boolean value is missing or invalid.");
            case HotFilterValueKind.Integer:
                if (TryReadLong(element, "Integer", out integer))
                    return true;
                return Invalid("Hot filter integer value is missing or invalid.");
            case HotFilterValueKind.UnsignedInteger:
                if (TryReadUInt64(element, "UnsignedInteger", out unsignedInteger))
                    return true;
                return Invalid("Hot filter unsigned integer value is missing or invalid.");
            case HotFilterValueKind.Number:
                if (TryReadDouble(element, "Number", out number) &&
                    !double.IsNaN(number) &&
                    !double.IsInfinity(number))
                {
                    return true;
                }

                return Invalid("Hot filter numeric value must be finite.");
            case HotFilterValueKind.Decimal:
                if (TryReadDecimal(element, "Decimal", out exactDecimal))
                    return true;
                return Invalid("Hot filter decimal value is missing or invalid.");
            case HotFilterValueKind.String:
                if (TryReadStringOrNull(element, "String", out text))
                    return true;
                return Invalid("Hot filter string value is invalid.");
            case HotFilterValueKind.Guid:
                if (TryReadGuid(element, out guid))
                    return true;
                return Invalid("Hot filter GUID value is missing or invalid.");
            case HotFilterValueKind.Timestamp:
                if (TryReadTimestamp(element, "Timestamp", out timestampTicks, out timestampText))
                    return true;
                return Invalid("Hot filter timestamp value is missing or invalid.");
            default:
                return Invalid("Hot filter value kind is missing or invalid.");
        }

        bool Invalid(string message)
        {
            Add(diagnostics, "FSFHOT009", path, message);
            return false;
        }
    }

    private static bool TryReadBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out JsonElement item))
            return false;
        if (item.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        return item.ValueKind == JsonValueKind.False;
    }

    private static bool TryReadLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement item) &&
            item.TryGetInt64(out value);
    }

    private static bool TryReadUInt64(JsonElement element, string name, out ulong value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement item) &&
            item.TryGetUInt64(out value);
    }

    private static bool TryReadDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement item) &&
            item.TryGetDouble(out value);
    }

    private static bool TryReadDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement item) &&
            item.TryGetDecimal(out value);
    }

    private static bool TryReadStringOrNull(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out JsonElement item) ||
            item.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (item.ValueKind != JsonValueKind.String)
            return false;

        value = item.GetString();
        return true;
    }

    private static bool TryReadGuid(JsonElement element, out string value)
    {
        value = string.Empty;
        string? raw = ReadNullableString(element, "Guid");
        if (string.IsNullOrWhiteSpace(raw) ||
            !Guid.TryParse(raw, out Guid parsed))
        {
            return false;
        }

        value = parsed.ToString("D");
        return true;
    }

    private static bool TryReadTimestamp(
        JsonElement element,
        string name,
        out long utcTicks,
        out string text)
    {
        utcTicks = 0;
        text = string.Empty;
        if (!element.TryGetProperty(name, out JsonElement item) ||
            !item.TryGetDateTimeOffset(out DateTimeOffset timestamp))
        {
            return false;
        }

        utcTicks = timestamp.UtcTicks;
        text = timestamp.ToString("o", CultureInfo.InvariantCulture);
        return true;
    }
}

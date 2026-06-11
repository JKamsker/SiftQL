using System.Collections.Immutable;
using System.Text.Json;

namespace SiftQL.Generators.Hot;

internal static partial class HotManifestParser
{
    private const int MaxFilterOperator = 8;

    private static HotFilterNode? ParseFilter(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        if (!TryReadEnum(element, "Kind", out HotFilterNodeKind kind))
        {
            Add(diagnostics, "FSFHOT009", path, "Hot filter node kind is missing or invalid.");
            return null;
        }

        int op = ReadInt(element, "Operator");
        if (kind == HotFilterNodeKind.Compare)
        {
            if (!TryReadOperator(element, out op))
            {
                Add(diagnostics, "FSFHOT009", path, "Hot filter compare operator is missing or invalid.");
                return null;
            }
        }

        HotFilterValue? value = null;
        if (element.TryGetProperty("Value", out JsonElement valueElement) &&
            valueElement.ValueKind != JsonValueKind.Null)
        {
            if (!RequireObject(valueElement, path, "Hot filter value", diagnostics))
                return null;

            value = ParseValue(valueElement, path, diagnostics);
            if (value is null)
                return null;
        }

        EquatableArray<HotFilterValue> values = ParseValues(element, path, diagnostics, out bool valuesValid);
        EquatableArray<HotFilterNode> children = ParseChildren(element, path, diagnostics, out bool childrenValid);
        if (!valuesValid || !childrenValid)
            return null;

        return new(
            kind,
            ReadString(element, "Field"),
            op,
            ReadBoolean(element, "IgnoreCase"),
            value,
            values,
            children);
    }

    private static EquatableArray<HotFilterNode> ParseChildren(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        out bool valid)
    {
        valid = true;
        if (!element.TryGetProperty("Children", out JsonElement items))
        {
            return EquatableArray<HotFilterNode>.Empty;
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, "FSFHOT009", path, "Hot filter children must be an array.");
            valid = false;
            return EquatableArray<HotFilterNode>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<HotFilterNode>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!RequireObject(item, path, "Hot filter child", diagnostics))
            {
                valid = false;
                continue;
            }

            HotFilterNode? child = ParseFilter(item, path, diagnostics);
            if (child is null)
            {
                valid = false;
                continue;
            }

            builder.Add(child);
        }

        return new(builder.ToImmutable());
    }

    private static EquatableArray<HotFilterValue> ParseValues(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        out bool valid)
    {
        valid = true;
        if (!element.TryGetProperty("Values", out JsonElement items))
        {
            return EquatableArray<HotFilterValue>.Empty;
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, "FSFHOT009", path, "Hot filter values must be an array.");
            valid = false;
            return EquatableArray<HotFilterValue>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<HotFilterValue>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!RequireObject(item, path, "Hot filter value", diagnostics))
            {
                valid = false;
                continue;
            }

            HotFilterValue? value = ParseValue(item, path, diagnostics);
            if (value is null)
            {
                valid = false;
                continue;
            }

            builder.Add(value);
        }

        return new(builder.ToImmutable());
    }

    private static bool TryReadEnum<TEnum>(
        JsonElement element,
        string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (!element.TryGetProperty(name, out JsonElement item) ||
            !item.TryGetInt32(out int raw) ||
            !Enum.IsDefined(typeof(TEnum), raw))
        {
            return false;
        }

        value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
        return true;
    }

    private static bool TryReadOperator(JsonElement element, out int value)
    {
        value = 0;
        if (!element.TryGetProperty("Operator", out JsonElement item) ||
            !item.TryGetInt32(out int raw) ||
            raw is < 0 or > MaxFilterOperator)
        {
            return false;
        }

        value = raw;
        return true;
    }
}

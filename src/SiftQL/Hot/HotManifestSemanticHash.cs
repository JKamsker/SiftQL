using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SiftQL.Hot;

internal static class HotManifestSemanticHash
{
    public static string Compute(string manifestJson)
    {
        using JsonDocument document = JsonDocument.Parse(manifestJson);
        return Compute(document.RootElement);
    }

    public static string Compute(JsonElement manifest)
    {
        var builder = new StringBuilder();
        AppendManifest(builder, manifest);
        return Sha256(builder.ToString());
    }

    private static void AppendManifest(StringBuilder builder, JsonElement manifest)
    {
        builder.Append("manifest{");
        AppendStringProperty(builder, manifest, "Schema");
        AppendStringProperty(builder, manifest, "RuntimeVersion");
        AppendStringProperty(builder, manifest, "FilterEngineVersion");
        AppendStringProperty(builder, manifest, "GeneratorVersion");
        AppendEntries(builder, manifest);
        builder.Append('}');
    }

    private static void AppendEntries(StringBuilder builder, JsonElement manifest)
    {
        builder.Append("entries[");
        if (!manifest.TryGetProperty("Entries", out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            builder.Append("missing]");
            return;
        }

        var encoded = new List<string>();
        foreach (JsonElement entry in entries.EnumerateArray())
            encoded.Add(EntryKey(entry));

        encoded.Sort(StringComparer.Ordinal);
        builder.Append(encoded.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
        for (int i = 0; i < encoded.Count; i++)
            AppendPart(builder, encoded[i]);
        builder.Append(']');
    }

    private static string EntryKey(JsonElement entry)
    {
        var builder = new StringBuilder();
        if (entry.ValueKind != JsonValueKind.Object)
        {
            AppendCanonicalJson(builder, entry);
            return builder.ToString();
        }

        builder.Append("entry{");
        AppendStringProperty(builder, entry, "Kind");
        AppendStringProperty(builder, entry, "SubjectType");
        AppendStringProperty(builder, entry, "Fingerprint");
        builder.Append("Definition=");
        if (entry.TryGetProperty("Definition", out JsonElement definition))
            AppendCanonicalJson(builder, definition);
        else
            builder.Append("missing");
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendStringProperty(
        StringBuilder builder,
        JsonElement element,
        string propertyName)
    {
        builder.Append(propertyName).Append('=');
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            AppendPart(builder, value.GetString() ?? string.Empty);
            return;
        }

        builder.Append("missing;");
    }

    private static void AppendCanonicalJson(StringBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                AppendObject(builder, value);
                break;
            case JsonValueKind.Array:
                AppendArray(builder, value);
                break;
            case JsonValueKind.String:
                builder.Append("s");
                AppendPart(builder, value.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                builder.Append("n");
                AppendPart(builder, value.GetRawText());
                break;
            case JsonValueKind.True:
                builder.Append("t;");
                break;
            case JsonValueKind.False:
                builder.Append("f;");
                break;
            case JsonValueKind.Null:
                builder.Append("null;");
                break;
            default:
                builder.Append("undefined;");
                break;
        }
    }

    private static void AppendObject(StringBuilder builder, JsonElement value)
    {
        JsonProperty[] properties = value
            .EnumerateObject()
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Append("o").Append(properties.Length.ToString(CultureInfo.InvariantCulture)).Append('{');
        for (int i = 0; i < properties.Length; i++)
        {
            AppendPart(builder, properties[i].Name);
            AppendCanonicalJson(builder, properties[i].Value);
        }

        builder.Append('}');
    }

    private static void AppendArray(StringBuilder builder, JsonElement value)
    {
        JsonElement[] items = value.EnumerateArray().ToArray();
        builder.Append("a").Append(items.Length.ToString(CultureInfo.InvariantCulture)).Append('[');
        for (int i = 0; i < items.Length; i++)
            AppendCanonicalJson(builder, items[i]);
        builder.Append(']');
    }

    private static void AppendPart(StringBuilder builder, string value) =>
        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');

    private static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}

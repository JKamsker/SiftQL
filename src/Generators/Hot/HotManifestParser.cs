using System.Collections.Immutable;
using System.Text.Json;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Hot;

internal static partial class HotManifestParser
{
    private const string ExpectedSchema = "siftql.hot.v1";
    private const string ExpectedEngine = "tiered-v1";
    private const string ExpectedGenerator = "hot-sourcegen-v1";

    public static bool IsCandidate(string path)
    {
        string file = Path.GetFileName(path);
        return file.Equals("siftql-filter-hot-manifest.json", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".siftql-hot.json", StringComparison.OrdinalIgnoreCase);
    }

    public static HotManifestParseResult Parse(AdditionalText text, CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<HotManifestEntry>();
        var diagnostics = ImmutableArray.CreateBuilder<HotProviderDiagnostic>();
        string manifestHash = string.Empty;
        string providerName = ProviderName(text.Path, manifestHash);
        string hintName = string.Concat(providerName, ".g.cs");

        try
        {
            string? json = text.GetText(cancellationToken)?.ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                Add(diagnostics, "FSFHOT001", text.Path, "Hot manifest is empty.");
                return Result(text.Path, providerName, hintName, manifestHash, entries, diagnostics);
            }

            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, "FSFHOT006", text.Path, "Hot manifest root must be a JSON object.");
                return Result(text.Path, providerName, hintName, manifestHash, entries, diagnostics);
            }

            manifestHash = HotManifestSemanticHash.Compute(root);
            providerName = ProviderName(text.Path, manifestHash);
            hintName = string.Concat(providerName, ".g.cs");
            if (!HasString(root, "Schema", ExpectedSchema))
                Add(diagnostics, "FSFHOT002", text.Path, $"Hot manifest schema must be '{ExpectedSchema}'.");
            if (!HasString(root, "FilterEngineVersion", ExpectedEngine))
                Add(diagnostics, "FSFHOT003", text.Path, $"Hot manifest engine must be '{ExpectedEngine}'.");
            if (!HasString(root, "GeneratorVersion", ExpectedGenerator))
                Add(diagnostics, "FSFHOT004", text.Path, $"Hot manifest generator must be '{ExpectedGenerator}'.");
            if (diagnostics.Count != 0)
                return Result(text.Path, providerName, hintName, manifestHash, entries, diagnostics);

            if (!root.TryGetProperty("Entries", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, "FSFHOT005", text.Path, "Hot manifest entries must be an array.");
                return Result(text.Path, providerName, hintName, manifestHash, entries, diagnostics);
            }

            int index = 0;
            foreach (JsonElement entry in items.EnumerateArray())
                ParseEntry(entry, text.Path, index++, entries, diagnostics);
        }
        catch (JsonException ex)
        {
            Add(diagnostics, "FSFHOT006", text.Path, "Hot manifest JSON is invalid: " + ex.Message);
        }

        return Result(text.Path, providerName, hintName, manifestHash, entries, diagnostics);
    }

    private static void ParseEntry(
        JsonElement element,
        string path,
        int index,
        ImmutableArray<HotManifestEntry>.Builder entries,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Add(diagnostics, "FSFHOT007", path, $"Hot manifest entry {index} must be a JSON object.");
            return;
        }

        string kind = ReadString(element, "Kind");
        string subjectType = ReadString(element, "SubjectType");
        string fingerprint = ReadString(element, "Fingerprint");
        if (string.IsNullOrWhiteSpace(kind) ||
            string.IsNullOrWhiteSpace(subjectType) ||
            string.IsNullOrWhiteSpace(fingerprint) ||
            !element.TryGetProperty("Definition", out JsonElement definition))
        {
            Add(diagnostics, "FSFHOT007", path, $"Hot manifest entry {index} is missing required metadata.");
            return;
        }

        if (kind.Equals("filter", StringComparison.OrdinalIgnoreCase))
        {
            if (!RequireObject(definition, path, "Hot filter definition", diagnostics))
                return;

            HotFilterNode? filter = ParseFilter(definition, path, diagnostics);
            if (filter is not null)
                entries.Add(new(HotEntryKind.Filter, subjectType, fingerprint, filter, null));
            return;
        }

        if (kind.Equals("projection", StringComparison.OrdinalIgnoreCase))
        {
            if (!RequireObject(definition, path, "Hot projection definition", diagnostics))
                return;

            HotProjection? projection = ParseProjection(definition, path, diagnostics);
            if (projection is not null)
                entries.Add(new(HotEntryKind.Projection, subjectType, fingerprint, null, projection));
            return;
        }

        Add(diagnostics, "FSFHOT008", path, $"Hot manifest entry {index} kind '{kind}' is not supported.");
    }

    private static HotManifestParseResult Result(
        string path,
        string providerName,
        string hintName,
        string manifestHash,
        ImmutableArray<HotManifestEntry>.Builder entries,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics) =>
        new(path, providerName, hintName, manifestHash, new(entries.ToImmutable()), new(diagnostics.ToImmutable()));

    private static bool HasString(JsonElement element, string name, string expected) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static string ReadString(JsonElement element, string name, string fallback = "") =>
        ReadNullableString(element, name) ?? fallback;

    private static string? ReadNullableString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int item) ? item : 0;

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long item) ? item : 0;

    private static ulong ReadUInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetUInt64(out ulong item) ? item : 0;

    private static double ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double item) ? item : 0;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.True;

    private static void Add(
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string id,
        string path,
        string message) =>
        diagnostics.Add(new(id, path, message));

    private static bool RequireObject(
        JsonElement element,
        string path,
        string label,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return true;

        Add(diagnostics, "FSFHOT009", path, label + " must be a JSON object.");
        return false;
    }

    private static string ProviderName(string path, string manifestHash)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var chars = name.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_');
        string identity = string.IsNullOrEmpty(manifestHash)
            ? Path.GetFileName(path)
            : string.Concat(Path.GetFileName(path), "|", manifestHash);
        return "GeneratedHotTieredProvider_" + string.Concat(chars) + "_" + StableHash(identity);
    }

    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return hash.ToString("X8");
    }

}

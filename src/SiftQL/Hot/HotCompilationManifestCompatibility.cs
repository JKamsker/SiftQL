using System.Text.Json;

namespace SiftQL.Hot;

internal static class HotCompilationManifestCompatibility
{
    public const string Schema = "siftql.hot.v1";
    public const string Engine = "tiered-v1";
    public const string Generator = "hot-sourcegen-v1";

    public static bool HasRequiredFields(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
            HasString(root, "Schema") &&
            HasString(root, "RuntimeVersion") &&
            HasString(root, "FilterEngineVersion") &&
            HasString(root, "GeneratorVersion") &&
            root.TryGetProperty("Entries", out JsonElement entries) &&
            entries.ValueKind == JsonValueKind.Array;
    }

    private static bool HasString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());
}

using System.Text.Json;

namespace SiftQL.Expressions;

// Versioned envelope for persisting/transmitting filters. Wrapping a filter in a
// document records the wire-format version so a future format change is detected
// instead of silently misread. The reader is tolerant: it also accepts a legacy
// bare FilterExpression with no envelope.
public sealed record FilterDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public FilterExpression Filter { get; init; } = FilterExpression.Any;

    public static FilterDocument Wrap(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new FilterDocument { Filter = filter };
    }

    public static string Serialize(FilterExpression filter, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return JsonSerializer.Serialize(Wrap(filter), options);
    }

    public static FilterExpression Deserialize(string json, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new FilterSerializationException("Filter document is not valid JSON.", ex);
        }

        // Envelope form: { "Version": n, "Filter": {...} }. The expected property
        // names honor the serializer's naming policy and case sensitivity, so an
        // envelope written with e.g. camelCase options is still recognized. Anything
        // else is treated as a legacy bare FilterExpression.
        string versionName = options?.PropertyNamingPolicy?.ConvertName("Version") ?? "Version";
        string filterName = options?.PropertyNamingPolicy?.ConvertName("Filter") ?? "Filter";
        if (root.ValueKind == JsonValueKind.Object &&
            IsEnvelopeLike(root, versionName, filterName, options, out JsonElement versionElement, out JsonElement filterElement))
        {
            if (versionElement.ValueKind == JsonValueKind.Undefined ||
                filterElement.ValueKind == JsonValueKind.Undefined)
            {
                throw new FilterSerializationException(
                    $"Filter document envelope requires both '{versionName}' and '{filterName}' properties.");
            }

            if (versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out int version))
            {
                throw new FilterSerializationException("Filter document version is missing or invalid.");
            }

            if (version < 1 || version > CurrentVersion)
            {
                throw new FilterSerializationException(
                    $"Filter document version {version} is not supported (this build reads up to {CurrentVersion}).");
            }

            return DeserializeFilter(filterElement.GetRawText(), options);
        }

        return DeserializeFilter(json, options);
    }

    private static bool IsEnvelopeLike(
        JsonElement root,
        string versionName,
        string filterName,
        JsonSerializerOptions? options,
        out JsonElement versionElement,
        out JsonElement filterElement)
    {
        bool hasVersion = TryGetEnvelopeProperty(root, versionName, options, out versionElement);
        bool hasFilter = TryGetEnvelopeProperty(root, filterName, options, out filterElement);
        return hasVersion || hasFilter;
    }

    private static bool TryGetEnvelopeProperty(
        JsonElement root,
        string name,
        JsonSerializerOptions? options,
        out JsonElement value)
    {
        if (options?.PropertyNameCaseInsensitive == true)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        return root.TryGetProperty(name, out value);
    }

    private static FilterExpression DeserializeFilter(string json, JsonSerializerOptions? options)
    {
        FilterExpression? filter;
        try
        {
            filter = JsonSerializer.Deserialize<FilterExpression>(json, options);
        }
        catch (JsonException ex)
        {
            throw new FilterSerializationException("Filter document contains an invalid filter.", ex);
        }

        return filter ?? throw new FilterSerializationException("Filter document filter was null.");
    }
}

public sealed class FilterSerializationException : InvalidOperationException
{
    public FilterSerializationException(string message)
        : base(message)
    {
    }

    public FilterSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

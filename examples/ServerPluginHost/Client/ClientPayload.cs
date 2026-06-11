using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Client;

internal static class ClientPayload
{
    public static IReadOnlyDictionary<string, object?> From(ProjectedEvent projected)
    {
        ArgumentNullException.ThrowIfNull(projected);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ProjectedEvent.EventType)] = projected.EventType,
            [nameof(ProjectedEvent.EventName)] = projected.EventName,
        };

        AddFields(projected.Fields, payload, overwrite: true);
        AddFields(projected.Context, payload, overwrite: false);
        return payload;
    }

    private static void AddFields(
        IEnumerable<ProjectedEventField>? fields,
        Dictionary<string, object?> payload,
        bool overwrite)
    {
        if (fields is null)
            return;

        foreach (ProjectedEventField? field in fields)
        {
            if (field is null || IsMetadataKey(field.Name))
                continue;

            if (overwrite)
                payload[field.Name] = ToObject(field.Value);
            else
                payload.TryAdd(field.Name, ToObject(field.Value));
        }
    }

    private static bool IsMetadataKey(string name) =>
        string.Equals(name, nameof(ProjectedEvent.EventType), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, nameof(ProjectedEvent.EventName), StringComparison.OrdinalIgnoreCase);

    private static object? ToObject(ProjectedEventValue? value)
    {
        if (value is null)
            return null;

        return value.Kind switch
        {
            ProjectedEventValueKind.Boolean => value.Boolean,
            ProjectedEventValueKind.Integer => value.Integer,
            ProjectedEventValueKind.UnsignedInteger => value.UnsignedInteger,
            ProjectedEventValueKind.Number => value.Number,
            ProjectedEventValueKind.Decimal => value.Decimal,
            ProjectedEventValueKind.String => value.String,
            ProjectedEventValueKind.Guid => value.Guid,
            ProjectedEventValueKind.Timestamp => value.Timestamp,
            ProjectedEventValueKind.Array => value.Values?.Select(ToObject).ToArray() ?? [],
            ProjectedEventValueKind.Object => ToDictionary(value.Fields),
            _ => null,
        };
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(
        IEnumerable<ProjectedEventField>? fields)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (fields is null)
            return values;

        foreach (ProjectedEventField? field in fields)
        {
            if (field is not null)
                values[field.Name] = ToObject(field.Value);
        }

        return values;
    }
}

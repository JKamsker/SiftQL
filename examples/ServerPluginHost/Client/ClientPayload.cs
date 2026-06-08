using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Client;

internal static class ClientPayload
{
    public static IReadOnlyDictionary<string, object?> From(ProjectedEvent projected)
    {
        ArgumentNullException.ThrowIfNull(projected);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ProjectedEvent.EventName)] = projected.EventName,
        };

        AddFields(projected.Fields, payload);
        AddFields(projected.Context, payload);
        return payload;
    }

    private static void AddFields(
        IEnumerable<ProjectedEventField> fields,
        Dictionary<string, object?> payload)
    {
        foreach (ProjectedEventField field in fields)
            payload[field.Name] = ToObject(field.Value);
    }

    private static object? ToObject(ProjectedEventValue value) =>
        value.Kind switch
        {
            ProjectedEventValueKind.Boolean => value.Boolean,
            ProjectedEventValueKind.Integer => value.Integer,
            ProjectedEventValueKind.UnsignedInteger => value.UnsignedInteger,
            ProjectedEventValueKind.Number => value.Number,
            ProjectedEventValueKind.Decimal => value.Decimal,
            ProjectedEventValueKind.String => value.String,
            ProjectedEventValueKind.Guid => value.Guid,
            ProjectedEventValueKind.Array => value.Values.Select(ToObject).ToArray(),
            ProjectedEventValueKind.Object => value.Fields.ToDictionary(
                static field => field.Name,
                static field => ToObject(field.Value),
                StringComparer.OrdinalIgnoreCase),
            _ => null,
        };
}

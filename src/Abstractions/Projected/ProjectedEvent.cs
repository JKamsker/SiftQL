using SiftQL.Translation;

namespace SiftQL.Projected;

public sealed record ProjectedEvent
{
    public string EventType { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public ProjectedEventField[] Fields { get; init; } = [];
    public ProjectedEventField[] Context { get; init; } = [];

    public bool TryGetField(string name, out ProjectedEventValue value) =>
        TryGet(Fields, name, out value);

    public bool TryGetContext(string name, out ProjectedEventValue value) =>
        TryGet(Context, name, out value);

    public ProjectedEventValue Field(string name) =>
        TryGetField(name, out var value) ? value : ProjectedEventValue.Null;

    public ProjectedEventValue ContextValue(string name) =>
        TryGetContext(name, out var value) ? value : ProjectedEventValue.Null;

    private static bool TryGet(
        IReadOnlyList<ProjectedEventField>? fields,
        string name,
        out ProjectedEventValue value)
    {
        if (fields is null)
        {
            value = ProjectedEventValue.Null;
            return false;
        }

        for (int i = 0; i < fields.Count; i++)
        {
            ProjectedEventField? field = fields[i];
            if (field is not null &&
                string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = field.Value;
                return true;
            }
        }

        value = ProjectedEventValue.Null;
        return false;
    }
}

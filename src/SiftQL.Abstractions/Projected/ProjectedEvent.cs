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
        IReadOnlyList<ProjectedEventField> fields,
        string name,
        out ProjectedEventValue value)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = fields[i].Value;
                return true;
            }
        }

        value = ProjectedEventValue.Null;
        return false;
    }
}

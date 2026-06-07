using SiftQL.Translation;

namespace SiftQL.Projected;

public sealed record ProjectedEventField
{
    public ProjectedEventField()
    {
    }

    public ProjectedEventField(string name, ProjectedEventValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; init; } = string.Empty;
    public ProjectedEventValue Value { get; init; } = ProjectedEventValue.Null;
}

using SiftQL.Translation;

namespace SiftQL.Expressions;

public sealed record EventProjectionField
{
    public EventProjectionField()
    {
    }

    public EventProjectionField(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Name = string.IsNullOrWhiteSpace(name) ? path : name;
    }

    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

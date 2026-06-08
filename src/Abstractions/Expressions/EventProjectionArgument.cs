using SiftQL.Translation;

namespace SiftQL.Expressions;

public enum EventProjectionArgumentKind
{
    Value = 0,
    SourceField = 1,
}

public sealed record EventProjectionArgument
{
    public EventProjectionArgument()
    {
    }

    public EventProjectionArgument(string name, FilterValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Kind = EventProjectionArgumentKind.Value;
    }

    public string Name { get; init; } = string.Empty;
    public EventProjectionArgumentKind Kind { get; init; }
    public FilterValue Value { get; init; } = FilterValue.Null;
    public string SourcePath { get; init; } = string.Empty;

    public static EventProjectionArgument From(string name, bool value) =>
        new(name, FilterValue.From(value));

    public static EventProjectionArgument From(string name, long value) =>
        new(name, FilterValue.From(value));

    public static EventProjectionArgument From(string name, double value) =>
        new(name, FilterValue.From(value));

    public static EventProjectionArgument From(string name, string value) =>
        new(name, FilterValue.From(value));

    public static EventProjectionArgument From(string name, Guid value) =>
        new(name, FilterValue.From(value));

    public static EventProjectionArgument FromSourceField(string name, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return new()
        {
            Name = name,
            Kind = EventProjectionArgumentKind.SourceField,
            SourcePath = sourcePath,
        };
    }
}

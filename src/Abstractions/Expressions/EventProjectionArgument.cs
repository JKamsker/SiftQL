using SiftQL.Translation;

namespace SiftQL.Expressions;

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
    }

    public string Name { get; init; } = string.Empty;
    public FilterValue Value { get; init; } = FilterValue.Null;

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
}

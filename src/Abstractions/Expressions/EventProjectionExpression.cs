using SiftQL.Translation;

namespace SiftQL.Expressions;

public sealed record EventProjectionExpression
{
    public static EventProjectionExpression Default { get; } = new();

    public EventProjectionField[] Fields { get; init; } = [];
    public EventProjectionInclude[] Includes { get; init; } = [];

    public bool IsDefault => Fields.Length == 0 && Includes.Length == 0;

    public EventProjectionExpression WithFields(IReadOnlyCollection<EventProjectionField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return this with { Fields = fields.ToArray() };
    }

    public EventProjectionExpression WithIncludes(IReadOnlyCollection<EventProjectionInclude> includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        return this with { Includes = Includes.Concat(includes).ToArray() };
    }

    public static EventProjectionExpression Select(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Default.WithFields(fields.Select(static field => new EventProjectionField(field)).ToArray());
    }
}

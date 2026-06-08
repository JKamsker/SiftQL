using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Projection;

public static class ProjectedEventFilterSchema
{
    public static FilterSchema ForFilter(FilterExpression expression)
    {
        var fields = BaseFields();
        CollectFilterFields(expression, fields);
        return new FilterSchema(typeof(ProjectedEvent), fields.Values.ToArray());
    }

    public static FilterSchema ForProjection(EventProjectionExpression projection)
    {
        var fields = BaseFields();
        for (int i = 0; i < projection.Fields.Length; i++)
        {
            if (projection.Fields[i] is not null)
                AddDynamicField(fields, projection.Fields[i].Path);
        }

        return new FilterSchema(typeof(ProjectedEvent), fields.Values.ToArray());
    }

    public static FilterField CreateField(string path) =>
        BaseFields().TryGetValue(path, out var field) ? field : DynamicField(path);

    private static Dictionary<string, FilterField> BaseFields() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ProjectedEvent.EventType)] = new(
                nameof(ProjectedEvent.EventType),
                typeof(string),
                FilterFieldKind.Scalar,
                static subject => ((ProjectedEvent)subject).EventType,
                ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(((ProjectedEvent)subject).EventType),
                Access: FilterFieldAccess.ForProperty(nameof(ProjectedEvent.EventType))),
            [nameof(ProjectedEvent.EventName)] = new(
                nameof(ProjectedEvent.EventName),
                typeof(string),
                FilterFieldKind.Scalar,
                static subject => ((ProjectedEvent)subject).EventName,
                ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(((ProjectedEvent)subject).EventName),
                Access: FilterFieldAccess.ForProperty(nameof(ProjectedEvent.EventName))),
            ["subjectType"] = new(
                "subjectType",
                typeof(string),
                FilterFieldKind.Scalar,
                static subject => ((ProjectedEvent)subject).EventType,
                ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(((ProjectedEvent)subject).EventType),
                Access: FilterFieldAccess.ForProperty(nameof(ProjectedEvent.EventType))),
            ["subjectName"] = new(
                "subjectName",
                typeof(string),
                FilterFieldKind.Scalar,
                static subject => ((ProjectedEvent)subject).EventName,
                ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(((ProjectedEvent)subject).EventName),
                Access: FilterFieldAccess.ForProperty(nameof(ProjectedEvent.EventName))),
        };

    private static void CollectFilterFields(
        FilterExpression expression,
        Dictionary<string, FilterField> fields)
    {
        AddDynamicField(fields, expression.Field);
        for (int i = 0; i < expression.Children.Length; i++)
            CollectFilterFields(expression.Children[i], fields);
    }

    private static void AddDynamicField(Dictionary<string, FilterField> fields, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || fields.ContainsKey(path))
            return;
        fields.Add(path, DynamicField(path));
    }

    private static FilterField DynamicField(string path)
    {
        if (!ProjectedEventPaths.TrySplit(path, out bool context, out string name))
            throw new FilterValidationException($"Projected filter field '{path}' is not a projected field path.");

        return new FilterField(
            path,
            typeof(ProjectedEventValue),
            FilterFieldKind.Scalar,
            subject => ToObject(Value((ProjectedEvent)subject, context, name)),
            ProjectionAccessor: subject => Value((ProjectedEvent)subject, context, name));
    }

    private static ProjectedEventValue Value(ProjectedEvent projected, bool context, string name)
    {
        string[] segments = name.Split('.');
        if (segments.Length == 0)
            return ProjectedEventValue.Null;

        ProjectedEventValue value = context
            ? projected.ContextValue(segments[0])
            : projected.Field(segments[0]);
        for (int i = 1; i < segments.Length; i++)
            value = ObjectField(value, segments[i]);

        return value;
    }

    private static ProjectedEventValue ObjectField(ProjectedEventValue value, string name)
    {
        if (value.Kind != ProjectedEventValueKind.Object)
            return ProjectedEventValue.Null;

        for (int i = 0; i < value.Fields.Length; i++)
        {
            ProjectedEventField field = value.Fields[i];
            if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                return field.Value;
        }

        return ProjectedEventValue.Null;
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
            ProjectedEventValueKind.Object => value,
            _ => null,
        };
}

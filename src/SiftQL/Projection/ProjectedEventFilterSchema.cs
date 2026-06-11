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

        for (int i = 0; i < projection.Includes.Length; i++)
            AddIncludeSourceFields(fields, projection.Includes[i]);

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

    private static void AddIncludeSourceFields(
        Dictionary<string, FilterField> fields,
        EventProjectionInclude? include)
    {
        if (include?.Arguments is not { } arguments)
            return;

        for (int i = 0; i < arguments.Length; i++)
        {
            EventProjectionArgument? argument = arguments[i];
            if (argument?.Kind == EventProjectionArgumentKind.SourceField)
                AddDynamicField(fields, argument.SourcePath);
        }
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
        if (TryExactValue(projected, context, name, out ProjectedEventValue exact))
            return exact;

        string[] segments = name.Split('.');
        if (segments.Length == 0)
            return ProjectedEventValue.Null;
        if (TryPrefixedValue(projected, context, segments, out ProjectedEventValue prefixed))
            return prefixed;

        ProjectedEventValue value = context
            ? projected.ContextValue(segments[0])
            : projected.Field(segments[0]);
        for (int i = 1; i < segments.Length; i++)
            value = ObjectField(value, segments[i]);

        return value;
    }

    private static bool TryExactValue(
        ProjectedEvent projected,
        bool context,
        string name,
        out ProjectedEventValue value) =>
        context
            ? projected.TryGetContext(name, out value)
            : projected.TryGetField(name, out value);

    private static bool TryPrefixedValue(
        ProjectedEvent projected,
        bool context,
        string[] segments,
        out ProjectedEventValue value)
    {
        for (int count = segments.Length - 1; count > 0; count--)
        {
            string prefix = string.Join(".", segments, 0, count);
            bool found = context
                ? projected.TryGetContext(prefix, out value)
                : projected.TryGetField(prefix, out value);
            if (!found)
                continue;

            for (int i = count; i < segments.Length; i++)
                value = ObjectField(value, segments[i]);
            return true;
        }

        value = ProjectedEventValue.Null;
        return false;
    }

    private static ProjectedEventValue ObjectField(ProjectedEventValue value, string name)
    {
        if (value.Kind != ProjectedEventValueKind.Object)
            return ProjectedEventValue.Null;
        if (value.Fields is not { Length: > 0 } fields)
            return ProjectedEventValue.Null;

        for (int i = 0; i < fields.Length; i++)
        {
            ProjectedEventField? field = fields[i];
            if (field is not null &&
                string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return field.Value;
            }
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
            ProjectedEventValueKind.Timestamp => value.Timestamp,
            ProjectedEventValueKind.Array => value.Values?.Select(ToObject).ToArray() ?? [],
            ProjectedEventValueKind.Object => value,
            _ => null,
        };
}

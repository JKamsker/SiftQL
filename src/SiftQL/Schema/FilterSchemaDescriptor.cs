using System.Text.Json.Nodes;
using SiftQL.Projected;

namespace SiftQL.Schema;

// Serializable description of a subject's filterable surface, for clients that
// build filter UIs or validate filters out-of-process. Projects the schema's
// fields, types, and the operators valid for each field type.
public sealed record FilterSchemaDescriptor(
    string SubjectType,
    IReadOnlyList<FilterFieldDescriptor> Fields)
{
    // Emits a JSON Schema (2020-12) object describing the filterable fields and,
    // via the x-siftql-operators annotation, the operators valid for each.
    public JsonObject ToJsonSchema()
    {
        var properties = new JsonObject();
        foreach (FilterFieldDescriptor field in Fields)
            properties[field.Name] = field.ToJsonSchemaProperty();

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = SubjectType,
            ["type"] = "object",
            ["properties"] = properties,
        };
    }
}

public sealed record FilterFieldDescriptor(
    string Name,
    string Type,
    FilterFieldKind Kind,
    string ScalarKind,
    IReadOnlyList<string> Operators)
{
    internal static FilterFieldDescriptor Describe(FilterField field) =>
        new(
            field.Name,
            field.ValueType.FullName ?? field.ValueType.Name,
            field.Kind,
            ScalarKindOf(field),
            OperatorsFor(field));

    internal JsonObject ToJsonSchemaProperty()
    {
        var property = new JsonObject();
        string? jsonType = JsonType(Kind, ScalarKind);
        if (jsonType is not null)
            property["type"] = jsonType;
        if (string.Equals(ScalarKind, "Temporal", StringComparison.Ordinal))
            property["format"] = "date-time";
        if (string.Equals(ScalarKind, "Guid", StringComparison.Ordinal))
            property["format"] = "uuid";

        var operators = new JsonArray();
        foreach (string op in Operators)
            operators.Add(op);
        property["x-siftql-operators"] = operators;
        return property;
    }

    private static string? JsonType(FilterFieldKind kind, string scalarKind) =>
        kind switch
        {
            FilterFieldKind.Array => "array",
            FilterFieldKind.Object => "object",
            _ => scalarKind switch
            {
                "Number" => "number",
                "Boolean" => "boolean",
                "String" or "Guid" or "Enum" or "Temporal" => "string",
                _ => null, // dynamic / unknown -> unconstrained
            },
        };

    private static string ScalarKindOf(FilterField field)
    {
        if (field.ValueType == typeof(ProjectedEventValue))
            return "Dynamic";
        if (IsTemporal(field.ValueType))
            return "Temporal";
        return field.ScalarAccessor?.Kind.ToString() ?? "None";
    }

    private static IReadOnlyList<string> OperatorsFor(FilterField field)
    {
        if (field.Kind == FilterFieldKind.Array)
            return ["Contains", "Count", "Exists"];
        if (field.Kind == FilterFieldKind.Object)
            return ["Exists"];

        if (field.ValueType == typeof(ProjectedEventValue))
        {
            return
            [
                "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan",
                "LessThanOrEqual", "Between", "StringContains", "StringStartsWith",
                "StringEndsWith", "In", "Exists",
            ];
        }

        if (IsTemporal(field.ValueType))
        {
            return
            [
                "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual",
                "LessThan", "LessThanOrEqual", "Between", "In", "Exists",
            ];
        }

        return field.ScalarAccessor?.Kind switch
        {
            FilterScalarKind.Number =>
            [
                "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual",
                "LessThan", "LessThanOrEqual", "Between", "In", "Exists",
            ],
            FilterScalarKind.String =>
            [
                "Equal", "NotEqual", "StringContains", "StringStartsWith",
                "StringEndsWith", "In", "Exists",
            ],
            FilterScalarKind.Boolean => ["Equal", "NotEqual", "Exists"],
            FilterScalarKind.Guid => ["Equal", "NotEqual", "In", "Exists"],
            FilterScalarKind.Enum => ["Equal", "NotEqual", "In", "Exists"],
            _ => ["Equal", "NotEqual", "Exists"],
        };
    }

    private static bool IsTemporal(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly);
    }
}

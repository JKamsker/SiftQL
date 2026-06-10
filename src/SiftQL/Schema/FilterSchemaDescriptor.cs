using SiftQL.Projected;

namespace SiftQL.Schema;

// Serializable description of a subject's filterable surface, for clients that
// build filter UIs or validate filters out-of-process. Projects the schema's
// fields, types, and the operators valid for each field type.
public sealed record FilterSchemaDescriptor(
    string SubjectType,
    IReadOnlyList<FilterFieldDescriptor> Fields);

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

    private static string ScalarKindOf(FilterField field)
    {
        if (field.ValueType == typeof(ProjectedEventValue))
            return "Dynamic";
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
                "LessThanOrEqual", "StringContains", "StringStartsWith", "StringEndsWith",
                "In", "Exists",
            ];
        }

        return field.ScalarAccessor?.Kind switch
        {
            FilterScalarKind.Number =>
            [
                "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual",
                "LessThan", "LessThanOrEqual", "In", "Exists",
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
}

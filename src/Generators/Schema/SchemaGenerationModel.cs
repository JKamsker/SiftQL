namespace SiftQL.Generators.Schema;

internal enum GeneratedFieldKind
{
    Scalar,
    Array,
}

internal enum GeneratedScalarKind
{
    Boolean,
    Number,
    String,
    Guid,
    Enum,
}

internal sealed record GeneratedSchema(
    string TypeName,
    string MetadataName,
    string HelperName,
    EquatableArray<GeneratedField> Fields);

internal sealed record GeneratedProvider(
    EquatableArray<GeneratedSchema> Schemas,
    bool Emit);

internal sealed record GeneratedField(
    string Name,
    string Access,
    string ValueType,
    string PropertyType,
    GeneratedFieldKind FieldKind,
    GeneratedScalarKind ScalarKind,
    bool IsNullable,
    string? ArrayContainsMethod);

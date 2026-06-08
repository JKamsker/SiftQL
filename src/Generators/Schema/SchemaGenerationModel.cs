namespace SiftQL.Generators.Schema;

internal enum GeneratedFieldKind
{
    Scalar,
    Array,
    Object,
}

internal enum GeneratedScalarKind
{
    Object,
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
    EquatableArray<GeneratedField> Fields,
    string? ReservedFieldCollision);

internal sealed record GeneratedProvider(
    EquatableArray<GeneratedSchema> Schemas,
    bool Emit);

internal sealed record GeneratedField(
    string Name,
    string Access,
    string SafeAccess,
    string ValueType,
    string PropertyType,
    GeneratedFieldKind FieldKind,
    GeneratedScalarKind ScalarKind,
    bool IsNullable,
    bool AccessCanReturnNull,
    bool EmitsScalarAccessor,
    string? ArrayContainsMethod);

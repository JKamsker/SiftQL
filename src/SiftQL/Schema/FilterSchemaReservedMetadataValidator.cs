using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Projected;

namespace SiftQL.Schema;

internal static class FilterSchemaReservedMetadataValidator
{
    public static void Validate(Type subjectType, FilterSchema schema)
    {
        if (!TryCreateMetadataProbe(subjectType, out object? probe))
            return;

        bool hasDerivedProbe = ReservedMetadataDerivedProbe.TryCreate(subjectType, out object? derivedProbe);
        bool allowProjectionAccessor = subjectType.IsSealed || hasDerivedProbe;
        ValidateField(
            subjectType,
            schema,
            "subjectType",
            subjectType.FullName ?? subjectType.Name,
            probe,
            allowProjectionAccessor);
        ValidateField(
            subjectType,
            schema,
            "subjectName",
            subjectType.Name,
            probe,
            allowProjectionAccessor);
        if (!hasDerivedProbe)
            return;

        Type derivedType = derivedProbe.GetType();
        ValidateField(
            subjectType,
            schema,
            "subjectType",
            derivedType.FullName ?? derivedType.Name,
            derivedProbe,
            allowProjectionAccessor: true);
        ValidateField(
            subjectType,
            schema,
            "subjectName",
            derivedType.Name,
            derivedProbe,
            allowProjectionAccessor: true);
    }

    private static void ValidateField(
        Type subjectType,
        FilterSchema schema,
        string name,
        string expected,
        object probe,
        bool allowProjectionAccessor)
    {
        if (!schema.TryGetField(name, out FilterField? field))
            return;
        if (field.Kind != FilterFieldKind.Scalar ||
            field.ValueType != typeof(string) ||
            !TryReadString(() => field.Getter(probe), out string? actual) ||
            !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw InvalidReservedMetadata(subjectType, name);
        }

        if (field.ScalarAccessor?.Text is { } text &&
            (!TryReadString(() => text(probe), out actual) ||
                !string.Equals(actual, expected, StringComparison.Ordinal)))
        {
            throw InvalidReservedMetadata(subjectType, name);
        }

        ValidateAccess(subjectType, field, name, expected);
        if (field.ProjectionAccessor is { } projectionAccessor &&
            (!allowProjectionAccessor ||
                !TryReadProjectedString(() => projectionAccessor(probe), out actual) ||
                !string.Equals(actual, expected, StringComparison.Ordinal)))
        {
            throw InvalidReservedMetadata(subjectType, name);
        }
    }

    private static void ValidateAccess(Type subjectType, FilterField field, string name, string expected)
    {
        if (field.Access is null)
            return;
        if (!subjectType.IsSealed || field.Access.PropertyPath is not null)
            throw InvalidReservedMetadata(subjectType, name);
        if (!TryReadString(() => field.Access.ConstantValue, out string? actual) ||
            !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw InvalidReservedMetadata(subjectType, name);
        }
    }

    private static bool TryCreateMetadataProbe(
        Type subjectType,
        [NotNullWhen(true)] out object? probe)
    {
        try
        {
            probe = subjectType.IsValueType
                ? Activator.CreateInstance(subjectType)
                : RuntimeHelpers.GetUninitializedObject(subjectType);
            return probe is not null;
        }
        catch
        {
            probe = null;
            return false;
        }
    }

    private static bool TryReadString(Func<object?> read, out string? value)
    {
        try
        {
            value = read() as string;
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool TryReadProjectedString(Func<ProjectedEventValue> read, out string? value)
    {
        try
        {
            ProjectedEventValue projected = read();
            value = projected.Kind == ProjectedEventValueKind.String ? projected.String : null;
            return value is not null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static FilterValidationException InvalidReservedMetadata(
        Type subjectType,
        string fieldName) =>
        new(
            $"Generated filter schema provider for '{subjectType.FullName}' returned invalid reserved metadata field '{fieldName}'.");
}

using System.Collections.Immutable;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderFieldValidator
{
    public static bool RequireField(
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        string name,
        bool? scalar,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        if (IsSubjectTypesMetadataField(fields, projectedEvent, name))
        {
            return scalar == true
                ? Unsupported(diagnostics, path, $"Hot entry field '{name}' is not scalar.")
                : true;
        }
        if (IsAllowedScalarMetadataField(projectedEvent, name))
        {
            return scalar == false
                ? Unsupported(diagnostics, path, $"Hot entry field '{name}' is not a scalar array.")
                : true;
        }
        if (projectedEvent && IsProjectedField(name))
            return true;

        GeneratedField? field = fields.Items.FirstOrDefault(
            item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (field is null)
            return Unsupported(diagnostics, path, $"Hot entry field '{name}' is not available in the schema.");
        if (scalar == true && field.FieldKind != GeneratedFieldKind.Scalar)
            return Unsupported(diagnostics, path, $"Hot entry field '{name}' is not scalar.");
        if (scalar == false && field.FieldKind != GeneratedFieldKind.Array)
            return Unsupported(diagnostics, path, $"Hot entry field '{name}' is not a scalar array.");
        return true;
    }

    public static bool IsMetadataField(string name) =>
        name.Equals("eventType", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("eventName", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("subjectType", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("subjectName", StringComparison.OrdinalIgnoreCase) ||
        IsSubjectTypesMetadataPath(name);

    private static bool IsAllowedScalarMetadataField(bool projectedEvent, string name) =>
        name.Equals("subjectType", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("subjectName", StringComparison.OrdinalIgnoreCase) ||
        projectedEvent &&
        (name.Equals("eventType", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("eventName", StringComparison.OrdinalIgnoreCase));

    private static bool IsSubjectTypesMetadataField(
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        string name)
    {
        if (projectedEvent)
            return false;

        if (name.Equals("subjectTypes", StringComparison.OrdinalIgnoreCase))
            return true;

        const string suffix = ".subjectTypes";
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string owner = name.Substring(0, name.Length - suffix.Length);
        return fields.Items.Any(field =>
            field.FieldKind == GeneratedFieldKind.Object &&
            string.Equals(field.Name, owner, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSubjectTypesMetadataPath(string name) =>
        name.Equals("subjectTypes", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".subjectTypes", StringComparison.OrdinalIgnoreCase);

    public static bool Unsupported(
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        string message)
    {
        diagnostics.Add(new("FSFHOT009", path, message));
        return false;
    }

    private static bool IsProjectedField(string name) =>
        HasProjectedPrefix(name, "field:") ||
        HasProjectedPrefix(name, "context:");

    private static bool HasProjectedPrefix(string name, string prefix) =>
        name.Length > prefix.Length &&
        name.StartsWith(prefix, StringComparison.Ordinal);
}

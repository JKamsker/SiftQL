using System.Collections.Immutable;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderProjectionResolver
{
    private const int MaxProjectionFields = 64;
    private const int MaxProjectionIncludes = 8;

    public static HotProjection? Resolve(
        HotProjection projection,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (projection.Includes.Count > MaxProjectionIncludes)
        {
            HotProviderFieldValidator.Unsupported(
                diagnostics,
                path,
                $"Hot projection exceeds the {MaxProjectionIncludes} include limit.");
            return null;
        }

        EquatableArray<HotProjectionField> requested = projection.Fields.Count == 0
            ? DefaultProjectionFields(fields, projectedEvent)
            : projection.Fields;
        if (requested.Count > MaxProjectionFields)
        {
            HotProviderFieldValidator.Unsupported(
                diagnostics,
                path,
                $"Hot projection exceeds the {MaxProjectionFields} field limit.");
            return null;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < requested.Count; i++)
        {
            HotProjectionField field = requested[i];
            if (!ValidateProjectionField(field, names, fields, projectedEvent, diagnostics, path))
                return null;
        }

        if (!ValidateProjectionIncludes(projection, fields, projectedEvent, diagnostics, path))
            return null;

        return projection with { Fields = requested };
    }

    private static bool ValidateProjectionIncludes(
        HotProjection projection,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        for (int i = 0; i < projection.Includes.Count; i++)
        {
            EquatableArray<HotProjectionArgument> arguments = projection.Includes[i].Arguments;
            for (int j = 0; j < arguments.Count; j++)
            {
                HotProjectionArgument argument = arguments[j];
                if (argument.Kind == HotProjectionArgumentKind.SourceField &&
                    !HotProviderFieldValidator.RequireField(
                        fields,
                        projectedEvent,
                        argument.SourcePath,
                        scalar: null,
                        path,
                        diagnostics))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateProjectionField(
        HotProjectionField field,
        HashSet<string> names,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (string.IsNullOrWhiteSpace(field.Name))
            return HotProviderFieldValidator.Unsupported(diagnostics, path, "Hot projection field name is required.");
        if (string.IsNullOrWhiteSpace(field.Path))
            return HotProviderFieldValidator.Unsupported(diagnostics, path, "Hot projection field path is required.");
        if (!names.Add(field.Name))
            return HotProviderFieldValidator.Unsupported(
                diagnostics,
                path,
                $"Hot projection field name '{field.Name}' is duplicated.");

        return HotProviderFieldValidator.RequireField(
            fields,
            projectedEvent,
            field.Path,
            scalar: null,
            path,
            diagnostics);
    }

    private static EquatableArray<HotProjectionField> DefaultProjectionFields(
        EquatableArray<GeneratedField> fields,
        bool projectedEvent) =>
        new(fields.Items
            .Where(field => field.FieldKind is GeneratedFieldKind.Scalar or GeneratedFieldKind.Array &&
                !field.UsesCollectionAccessor &&
                !IsDefaultProjectionMetadataField(projectedEvent, field.Name))
            .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static field => new HotProjectionField(field.Name, field.Name))
            .ToImmutableArray());

    private static bool IsDefaultProjectionMetadataField(bool projectedEvent, string name) =>
        name.Equals("subjectType", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("subjectName", StringComparison.OrdinalIgnoreCase) ||
        IsSubjectTypesDiscriminatorPath(name) ||
        projectedEvent &&
        (name.Equals("eventType", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("eventName", StringComparison.OrdinalIgnoreCase));

    private static bool IsSubjectTypesDiscriminatorPath(string name) =>
        name.Equals("subjectTypes", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".subjectTypes", StringComparison.OrdinalIgnoreCase);
}

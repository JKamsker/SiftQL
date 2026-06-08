using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderResolver
{
    private const int MaxProjectionFields = 64;
    private const int MaxProjectionIncludes = 8;
    private static readonly SymbolDisplayFormat s_format = SymbolDisplayFormat.FullyQualifiedFormat;

    public static HotProviderSource Resolve(
        Compilation compilation,
        HotManifestParseResult manifest,
        CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<HotProviderEntry>();
        var diagnostics = manifest.Diagnostics.Items.ToBuilder();
        for (int i = 0; i < manifest.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HotManifestEntry entry = manifest.Entries[i];
            INamedTypeSymbol? subject = HotSubjectTypeResolver.Resolve(compilation, entry.SubjectType);
            if (subject is null)
            {
                Add(diagnostics, manifest.Path, $"Hot entry subject '{entry.SubjectType}' cannot be resolved.");
                continue;
            }

            var fields = DiscoverFields(subject);
            bool projectedEvent = IsProjectedEvent(subject);
            if (entry.Kind == HotEntryKind.Filter && entry.Filter is not null)
            {
                if (HotProviderFilterValidator.Validate(entry.Filter, fields, projectedEvent, diagnostics, manifest.Path) &&
                    ValidateFingerprint(
                        HotManifestFingerprint.Filter(entry.Filter),
                        entry.Fingerprint,
                        diagnostics,
                        manifest.Path))
                {
                    entries.Add(new(entry.Kind, subject.ToDisplayString(s_format), entry.Fingerprint, entry.Filter, null));
                }

                continue;
            }

            if (entry.Kind == HotEntryKind.Projection && entry.Projection is not null)
            {
                if (!ValidateFingerprint(
                        HotManifestFingerprint.Projection(entry.Projection),
                        entry.Fingerprint,
                        diagnostics,
                        manifest.Path))
                {
                    continue;
                }

                HotProjection? projection = ResolveProjection(
                    entry.Projection,
                    fields,
                    projectedEvent,
                    diagnostics,
                    manifest.Path);
                if (projection is not null)
                {
                    string fingerprint = HotManifestFingerprint.Projection(projection);
                    entries.Add(new(entry.Kind, subject.ToDisplayString(s_format), fingerprint, null, projection));
                }
            }
        }

        return new HotProviderSource(
            manifest.ProviderName,
            manifest.HintName,
            manifest.ManifestHash,
            new(NormalizeEntries(entries.ToImmutable())),
            new(diagnostics.ToImmutable()));
    }

    private static ImmutableArray<HotProviderEntry> NormalizeEntries(ImmutableArray<HotProviderEntry> entries) =>
        entries
            .GroupBy(static entry => new { entry.Kind, entry.SubjectTypeName, entry.Fingerprint })
            .Select(static group => group.First())
            .OrderBy(static entry => entry.Kind)
            .ThenBy(static entry => entry.Fingerprint, StringComparer.Ordinal)
            .ThenBy(static entry => entry.SubjectTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

    private static EquatableArray<GeneratedField> DiscoverFields(INamedTypeSymbol subject)
    {
        var fields = ImmutableArray.CreateBuilder<GeneratedField>();
        SchemaFieldDiscovery.AddProperties(fields, string.Empty, string.Empty, string.Empty, subject, depth: 0);
        return new(fields.ToImmutable());
    }

    private static HotProjection? ResolveProjection(
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

        return projection with { Fields = requested };
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
                !IsDefaultProjectionMetadataField(projectedEvent, field.Name))
            .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static field => new HotProjectionField(field.Name, field.Name))
            .ToImmutableArray());

    private static bool IsDefaultProjectionMetadataField(bool projectedEvent, string name) =>
        name.Equals("subjectType", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("subjectName", StringComparison.OrdinalIgnoreCase) ||
        projectedEvent &&
        (name.Equals("eventType", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("eventName", StringComparison.OrdinalIgnoreCase));

    private static bool IsProjectedEvent(INamedTypeSymbol subject) =>
        subject.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
        "SiftQL.Projected.ProjectedEvent";

    private static bool ValidateFingerprint(
        string actual,
        string declared,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (string.Equals(actual, declared, StringComparison.Ordinal))
            return true;

        Add(diagnostics, path, "Hot entry fingerprint does not match its definition.");
        return false;
    }

    private static void Add(
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        string message) =>
        diagnostics.Add(new("FSFHOT009", path, message));
}

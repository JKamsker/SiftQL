using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderResolver
{
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
            INamedTypeSymbol? subject = ResolveSubject(compilation, entry.SubjectType);
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
                    entries.Add(new(entry.Kind, subject.ToDisplayString(s_format), entry.Fingerprint, null, projection));
            }
        }

        return new HotProviderSource(
            manifest.ProviderName,
            manifest.HintName,
            manifest.ManifestHash,
            new(entries.ToImmutable()),
            new(diagnostics.ToImmutable()));
    }

    private static INamedTypeSymbol? ResolveSubject(Compilation compilation, string subjectType)
    {
        string metadataName = subjectType.Split(',')[0].Trim();
        return compilation.GetTypeByMetadataName(metadataName) ??
            compilation.GetTypeByMetadataName(metadataName.Replace('+', '.'));
    }

    private static EquatableArray<GeneratedField> DiscoverFields(INamedTypeSymbol subject)
    {
        var fields = ImmutableArray.CreateBuilder<GeneratedField>();
        SchemaFieldDiscovery.AddProperties(fields, string.Empty, string.Empty, subject, depth: 0);
        return new(fields.ToImmutable());
    }

    private static HotProjection? ResolveProjection(
        HotProjection projection,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        EquatableArray<HotProjectionField> requested = projection.Fields.Count == 0
            ? DefaultProjectionFields(fields)
            : projection.Fields;
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
            scalar: true,
            path,
            diagnostics);
    }

    private static EquatableArray<HotProjectionField> DefaultProjectionFields(EquatableArray<GeneratedField> fields) =>
        new(fields.Items
            .Where(static field => field.FieldKind == GeneratedFieldKind.Scalar &&
                !HotProviderFieldValidator.IsMetadataField(field.Name))
            .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static field => new HotProjectionField(field.Name, field.Name))
            .ToImmutableArray());

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

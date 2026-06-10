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
            INamedTypeSymbol? subject = HotSubjectTypeResolver.Resolve(compilation, entry.SubjectType);
            if (subject is null)
            {
                Add(diagnostics, manifest.Path, $"Hot entry subject '{entry.SubjectType}' cannot be resolved.");
                continue;
            }

            bool projectedEvent = IsProjectedEvent(subject);
            if (!projectedEvent && !IsFilterSubject(subject))
            {
                Add(diagnostics, manifest.Path, $"Hot entry subject '{entry.SubjectType}' must implement SiftQL.IFilterSubject.");
                continue;
            }

            if (!projectedEvent && !SupportedSubjectShape(subject))
            {
                Add(diagnostics, manifest.Path, $"Hot entry subject '{entry.SubjectType}' has an unsupported type shape.");
                continue;
            }

            if (!projectedEvent &&
                SchemaFieldDiscovery.ReservedTopLevelPropertyCollision(subject) is { } collision)
            {
                Add(
                    diagnostics,
                    manifest.Path,
                    $"Hot entry subject '{entry.SubjectType}' property '{collision}' collides with reserved metadata field '{collision}'.");
                continue;
            }

            var fields = DiscoverFields(subject, CurrentGeneratedSchemaEligible(subject));
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

                HotProjection? projection = HotProviderProjectionResolver.Resolve(
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

    private static EquatableArray<GeneratedField> DiscoverFields(
        INamedTypeSymbol subject,
        bool includeGeneratedOnlyFields)
    {
        var fields = ImmutableArray.CreateBuilder<GeneratedField>();
        SchemaFieldDiscovery.AddProperties(fields, string.Empty, string.Empty, string.Empty, subject, depth: 0);
        return new(includeGeneratedOnlyFields
            ? fields.ToImmutable()
            : fields
                .Where(static field =>
                    field.FieldKind != GeneratedFieldKind.Object &&
                    !field.Name.Contains(".", StringComparison.Ordinal))
                .ToImmutableArray());
    }

    private static bool IsProjectedEvent(INamedTypeSymbol subject) =>
        subject.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
        "SiftQL.Projected.ProjectedEvent";

    private static bool IsFilterSubject(INamedTypeSymbol subject) =>
        IsFilterSubjectType(subject) ||
        subject.AllInterfaces.Any(IsFilterSubjectType);

    private static bool IsFilterSubjectType(INamedTypeSymbol subject) =>
        subject.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
        "SiftQL.IFilterSubject";

    private static bool CurrentGeneratedSchemaEligible(INamedTypeSymbol subject) =>
        IsExternallyVisible(subject) &&
        !subject.IsGenericType &&
        !subject.IsAbstract &&
        subject.TypeKind is TypeKind.Class or TypeKind.Struct;

    private static bool SupportedSubjectShape(INamedTypeSymbol subject) =>
        !ContainsTypeParameter(subject) &&
        !subject.IsRefLikeType;

    private static bool ContainsTypeParameter(ITypeSymbol symbol)
    {
        if (symbol.TypeKind == TypeKind.TypeParameter)
            return true;

        if (symbol is not INamedTypeSymbol named)
            return false;

        for (int i = 0; i < named.TypeArguments.Length; i++)
        {
            if (ContainsTypeParameter(named.TypeArguments[i]))
                return true;
        }

        return named.ContainingType is not null &&
            ContainsTypeParameter(named.ContainingType);
    }

    private static bool IsExternallyVisible(INamedTypeSymbol subject)
    {
        for (INamedTypeSymbol? current = subject; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
                return false;
        }

        return true;
    }

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

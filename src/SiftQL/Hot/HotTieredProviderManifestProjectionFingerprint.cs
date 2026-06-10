using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Hot;

internal static partial class HotTieredProviderManifestValidator
{
    private static string[] CandidateFingerprints(
        HotCompilationManifestEntry entry,
        Type subjectType)
    {
        if (!string.Equals(entry.Kind, "projection", StringComparison.OrdinalIgnoreCase) ||
            !TryEffectiveProjectionFingerprint(entry, subjectType, out string? fingerprint) ||
            string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return [entry.Fingerprint];
        }

        return [entry.Fingerprint, fingerprint];
    }

    private static bool TryEffectiveProjectionFingerprint(
        HotCompilationManifestEntry entry,
        Type subjectType,
        [NotNullWhen(true)] out string? fingerprint)
    {
        try
        {
            EventProjectionExpression? projection =
                entry.Definition.Deserialize<EventProjectionExpression>();
            if (projection is null)
            {
                fingerprint = null;
                return false;
            }

            EventProjectionExpression effective = projection.Fields.Length == 0
                ? projection with { Fields = DefaultProjectionFields(subjectType) }
                : projection;
            fingerprint = ProjectionExpressionFingerprint.Create(effective);
            return true;
        }
        catch
        {
            fingerprint = null;
            return false;
        }
    }

    private static EventProjectionField[] DefaultProjectionFields(Type subjectType)
    {
        FilterSchema schema = subjectType == typeof(ProjectedEvent)
            ? ProjectedEventFilterSchema.ForProjection(EventProjectionExpression.Default)
            : FilterSchema.For(subjectType);
        return schema.FieldNames
            .Where(name => IsDefaultProjectionField(schema, name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(static name => new EventProjectionField(name))
            .ToArray();
    }

    private static bool IsDefaultProjectionField(FilterSchema schema, string name) =>
        !IsVirtualMetadataField(schema.SubjectType, name) &&
        schema.TryGetField(name, out FilterField field) &&
        field.Kind != FilterFieldKind.Object &&
        !field.IsCollectionDerived;

    private static bool IsVirtualMetadataField(Type subjectType, string name) =>
        string.Equals(name, "subjectType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "subjectName", StringComparison.OrdinalIgnoreCase) ||
        SubjectTypeMetadata.IsDiscriminatorPath(name) ||
        subjectType == typeof(ProjectedEvent) &&
        (string.Equals(name, "eventType", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "eventName", StringComparison.OrdinalIgnoreCase));
}

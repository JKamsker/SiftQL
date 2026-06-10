using System.Runtime.Loader;

namespace SiftQL.Hot;

internal static partial class HotTieredProviderManifestValidator
{
    internal static HotTieredProviderManifestAllowList? CreateAllowList(
        HotCompilationManifest manifest,
        AssemblyLoadContext loadContext)
    {
        if (manifest.Entries.Length == 0)
            return null;

        var entries = new List<HotTieredProviderAllowedEntry>();
        for (int i = 0; i < manifest.Entries.Length; i++)
        {
            HotCompilationManifestEntry entry = manifest.Entries[i];
            if (!TryResolveSubjectType(loadContext, entry.SubjectType, out Type? subjectType) ||
                string.IsNullOrWhiteSpace(entry.Fingerprint) ||
                !TryEntryKind(entry.Kind, out HotTieredProviderAllowedEntryKind kind) ||
                !TryEntryHasParameters(entry, out bool hasParameters))
            {
                return null;
            }

            string[] fingerprints = CandidateFingerprints(entry, subjectType);
            for (int j = 0; j < fingerprints.Length; j++)
            {
                entries.Add(new(
                    kind,
                    subjectType,
                    fingerprints[j],
                    hasParameters));
            }
        }

        return entries.Count == 0 ? null : new(entries);
    }

    private static bool TryEntryKind(
        string kind,
        out HotTieredProviderAllowedEntryKind value)
    {
        if (string.Equals(kind, "filter", StringComparison.OrdinalIgnoreCase))
        {
            value = HotTieredProviderAllowedEntryKind.Filter;
            return true;
        }

        if (string.Equals(kind, "projection", StringComparison.OrdinalIgnoreCase))
        {
            value = HotTieredProviderAllowedEntryKind.Projection;
            return true;
        }

        value = default;
        return false;
    }
}

internal sealed class HotTieredProviderManifestAllowList
{
    private readonly HashSet<HotTieredProviderAllowedEntryKey> _filters = [];
    private readonly HashSet<HotTieredProviderAllowedEntryKey> _parameterizedFilters = [];
    private readonly HashSet<HotTieredProviderAllowedEntryKey> _projections = [];
    private readonly HashSet<HotTieredProviderAllowedEntryKey> _parameterizedProjections = [];

    public HotTieredProviderManifestAllowList(
        IReadOnlyCollection<HotTieredProviderAllowedEntry> entries)
    {
        Entries = entries.ToArray();
        for (int i = 0; i < Entries.Length; i++)
            Add(Entries[i]);
    }

    internal HotTieredProviderAllowedEntry[] Entries { get; }

    internal bool AllowsFilter(Type subjectType, string fingerprint, bool parameterized) =>
        Set(HotTieredProviderAllowedEntryKind.Filter, parameterized)
            .Contains(new(subjectType, fingerprint));

    internal bool AllowsProjection(Type subjectType, string fingerprint, bool parameterized) =>
        Set(HotTieredProviderAllowedEntryKind.Projection, parameterized)
            .Contains(new(subjectType, fingerprint));

    private void Add(HotTieredProviderAllowedEntry entry) =>
        Set(entry.Kind, entry.HasParameters)
            .Add(new(entry.SubjectType, entry.Fingerprint));

    private HashSet<HotTieredProviderAllowedEntryKey> Set(
        HotTieredProviderAllowedEntryKind kind,
        bool parameterized) =>
        kind switch
        {
            HotTieredProviderAllowedEntryKind.Filter => parameterized
                ? _parameterizedFilters
                : _filters,
            HotTieredProviderAllowedEntryKind.Projection => parameterized
                ? _parameterizedProjections
                : _projections,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private readonly record struct HotTieredProviderAllowedEntryKey(
        Type SubjectType,
        string Fingerprint);
}

internal readonly record struct HotTieredProviderAllowedEntry(
    HotTieredProviderAllowedEntryKind Kind,
    Type SubjectType,
    string Fingerprint,
    bool HasParameters);

internal enum HotTieredProviderAllowedEntryKind
{
    Filter,
    Projection,
}

namespace SiftQL.Index;

// Snapshot of how subscriptions are physically placed in the index, for routing
// observability. UnindexedCount rising is the signal that filters are silently
// falling back to full scans (e.g. after switching an Equal to a StringContains).
public sealed record FilterSubscriptionIndexStatistics(
    int Count,
    int IndexedCount,
    int UnindexedCount,
    IReadOnlyDictionary<string, int> BucketsByField);

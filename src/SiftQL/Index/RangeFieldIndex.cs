using SiftQL.Schema;

namespace SiftQL.Index;

// Live per-field range index for subscriptions whose most selective condition is
// an interval (Between / ordered Compare / merged And). Rebuilt into an immutable
// RangeFieldSnapshot on each mutation, like the equality field index.
internal sealed class RangeFieldIndex<TSubscription>
    where TSubscription : class
{
    private readonly Func<object, decimal?> _accessor;
    private readonly List<(RangeCondition Condition, SubscriptionEntry<TSubscription> Entry)> _entries = [];

    public RangeFieldIndex(FilterField field) =>
        _accessor = RangeKey.CreateAccessor(field);

    public bool IsEmpty => _entries.Count == 0;

    public int Count => _entries.Count;

    public void Add(RangeCondition condition, SubscriptionEntry<TSubscription> entry) =>
        _entries.Add((condition, entry));

    public bool Remove(SubscriptionEntry<TSubscription> entry)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i].Entry, entry))
            {
                _entries.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public RangeFieldSnapshot<TSubscription> ToSnapshot()
    {
        var lower = new List<(decimal Key, SubscriptionEntry<TSubscription> Entry)>();
        var upper = new List<(decimal Key, SubscriptionEntry<TSubscription> Entry)>();
        foreach ((RangeCondition condition, SubscriptionEntry<TSubscription> entry) in _entries)
        {
            // A finite lower bound narrows best (upper is re-checked by the full
            // predicate); pure upper-bounded conditions narrow on their upper.
            if (condition.Lower is { } lowerKey)
                lower.Add((lowerKey, entry));
            else if (condition.Upper is { } upperKey)
                upper.Add((upperKey, entry));
        }

        lower.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        upper.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        return new RangeFieldSnapshot<TSubscription>(
            _accessor,
            lower.Select(static item => item.Key).ToArray(),
            lower.Select(static item => item.Entry).ToArray(),
            upper.Select(static item => item.Key).ToArray(),
            upper.Select(static item => item.Entry).ToArray());
    }
}

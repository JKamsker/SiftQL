using SiftQL.Schema;

namespace SiftQL.Index;

internal sealed class TypedRangeFieldIndex<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly Func<object, decimal?> _accessor;
    private readonly List<(RangeCondition Condition, TypedSubscriptionEntry<TSubscription, TSubject> Entry)> _entries = [];

    public TypedRangeFieldIndex(FilterField field) =>
        _accessor = RangeKey.CreateAccessor(field);

    public bool IsEmpty => _entries.Count == 0;

    public int Count => _entries.Count;

    public void Add(RangeCondition condition, TypedSubscriptionEntry<TSubscription, TSubject> entry) =>
        _entries.Add((condition, entry));

    public bool Remove(TypedSubscriptionEntry<TSubscription, TSubject> entry)
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

    public TypedRangeFieldSnapshot<TSubscription, TSubject> ToSnapshot()
    {
        var lower = new List<(decimal Key, TypedSubscriptionEntry<TSubscription, TSubject> Entry)>();
        var upper = new List<(decimal Key, TypedSubscriptionEntry<TSubscription, TSubject> Entry)>();
        foreach ((RangeCondition condition, TypedSubscriptionEntry<TSubscription, TSubject> entry) in _entries)
        {
            if (condition.Lower is { } lowerKey)
                lower.Add((lowerKey, entry));
            else if (condition.Upper is { } upperKey)
                upper.Add((upperKey, entry));
        }

        lower.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        upper.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        return new TypedRangeFieldSnapshot<TSubscription, TSubject>(
            _accessor,
            lower.Select(static item => item.Key).ToArray(),
            lower.Select(static item => item.Entry).ToArray(),
            upper.Select(static item => item.Key).ToArray(),
            upper.Select(static item => item.Entry).ToArray());
    }
}

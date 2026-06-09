using SiftQL.Schema;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionFieldIndex<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly Func<TSubject, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, SubscriptionBucket<TypedSubscriptionEntry<TSubscription, TSubject>>> _byValue = [];

    public TypedSubscriptionFieldIndex(FilterField field) =>
        _accessor = FilterIndexValueAccessor<TSubject>.Create(field);

    public bool IsEmpty => _byValue.Count == 0;

    public void Add(FilterIndexValue value, TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        if (!_byValue.TryGetValue(value, out var bucket))
        {
            bucket = new SubscriptionBucket<TypedSubscriptionEntry<TSubscription, TSubject>>();
            _byValue.Add(value, bucket);
        }

        bucket.Add(entry);
    }

    public bool Remove(FilterIndexValue value, TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        if (!_byValue.TryGetValue(value, out var bucket) ||
            !bucket.Remove(entry))
        {
            return false;
        }

        if (bucket.Count == 0)
            _byValue.Remove(value);
        return true;
    }

    public TypedSubscriptionFieldSnapshot<TSubscription, TSubject> ToSnapshot()
    {
        var byValue = new Dictionary<FilterIndexValue, TypedSubscriptionEntry<TSubscription, TSubject>[]>(_byValue.Count);
        foreach (var pair in _byValue)
            byValue.Add(pair.Key, pair.Value.Snapshot());
        return new(_accessor, byValue);
    }
}

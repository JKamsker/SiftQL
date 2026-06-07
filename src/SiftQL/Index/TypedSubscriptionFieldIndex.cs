using SiftQL.Schema;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionFieldIndex<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly Func<TSubject, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, TypedSubscriptionEntry<TSubscription, TSubject>[]> _byValue = [];

    public TypedSubscriptionFieldIndex(FilterField field) =>
        _accessor = FilterIndexValueAccessor<TSubject>.Create(field);

    public void Add(FilterIndexValue value, TypedSubscriptionEntry<TSubscription, TSubject> entry) =>
        _byValue[value] = _byValue.TryGetValue(value, out TypedSubscriptionEntry<TSubscription, TSubject>[]? items)
            ? SubscriptionIndexArrays.Add(items, entry)
            : [entry];

    public bool Remove(FilterIndexValue value, TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        if (!_byValue.TryGetValue(value, out TypedSubscriptionEntry<TSubscription, TSubject>[]? items))
            return false;

        TypedSubscriptionEntry<TSubscription, TSubject>[]? next = SubscriptionIndexArrays.Remove(items, entry);
        if (next is null)
            return false;
        if (next.Length == 0)
            _byValue.Remove(value);
        else
            _byValue[value] = next;
        return true;
    }

    public TypedSubscriptionFieldSnapshot<TSubscription, TSubject> ToSnapshot() =>
        new(_accessor, new Dictionary<FilterIndexValue, TypedSubscriptionEntry<TSubscription, TSubject>[]>(_byValue));
}

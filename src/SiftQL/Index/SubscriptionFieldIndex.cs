using System.Reflection;
using SiftQL.Schema;

namespace SiftQL.Index;

internal sealed class SubscriptionFieldIndex<TSubscription>
    where TSubscription : class
{
    private readonly Func<object, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, SubscriptionBucket<SubscriptionEntry<TSubscription>>> _byValue = [];

    public SubscriptionFieldIndex(Type subjectType, FilterField field) =>
        _accessor = CreateAccessor(subjectType, field);

    public bool IsEmpty => _byValue.Count == 0;

    public int BucketCount => _byValue.Count;

    public void Add(FilterIndexValue value, SubscriptionEntry<TSubscription> entry)
    {
        if (!_byValue.TryGetValue(value, out var bucket))
        {
            bucket = new SubscriptionBucket<SubscriptionEntry<TSubscription>>();
            _byValue.Add(value, bucket);
        }

        bucket.Add(entry);
    }

    public bool Remove(FilterIndexValue value, SubscriptionEntry<TSubscription> entry)
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

    public SubscriptionFieldSnapshot<TSubscription> ToSnapshot()
    {
        var byValue = new Dictionary<FilterIndexValue, SubscriptionEntry<TSubscription>[]>(_byValue.Count);
        foreach (var pair in _byValue)
            byValue.Add(pair.Key, pair.Value.Snapshot());
        return new(_accessor, byValue);
    }

    private static Func<object, FilterIndexValue?> CreateAccessor(Type subjectType, FilterField field)
    {
        MethodInfo method = typeof(SubscriptionFieldIndex<TSubscription>)
            .GetMethod(nameof(CreateTypedAccessor), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(subjectType);
        return (Func<object, FilterIndexValue?>)method.Invoke(null, [field])!;
    }

    private static Func<object, FilterIndexValue?> CreateTypedAccessor<TSubject>(FilterField field)
    {
        Func<TSubject, FilterIndexValue?> accessor = FilterIndexValueAccessor<TSubject>.Create(field);
        return subject => subject is TSubject typed ? accessor(typed) : null;
    }
}

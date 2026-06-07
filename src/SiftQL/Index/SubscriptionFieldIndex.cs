using System.Reflection;
using SiftQL.Schema;

namespace SiftQL.Index;

internal sealed class SubscriptionFieldIndex<TSubscription>
    where TSubscription : class
{
    private readonly Func<object, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, SubscriptionEntry<TSubscription>[]> _byValue = [];

    public SubscriptionFieldIndex(Type subjectType, FilterField field) =>
        _accessor = CreateAccessor(subjectType, field);

    public void Add(FilterIndexValue value, SubscriptionEntry<TSubscription> entry) =>
        _byValue[value] = _byValue.TryGetValue(value, out SubscriptionEntry<TSubscription>[]? items)
            ? SubscriptionIndexArrays.Add(items, entry)
            : [entry];

    public bool Remove(FilterIndexValue value, SubscriptionEntry<TSubscription> entry)
    {
        if (!_byValue.TryGetValue(value, out SubscriptionEntry<TSubscription>[]? items))
            return false;

        SubscriptionEntry<TSubscription>[]? next = SubscriptionIndexArrays.Remove(items, entry);
        if (next is null)
            return false;
        if (next.Length == 0)
            _byValue.Remove(value);
        else
            _byValue[value] = next;
        return true;
    }

    public SubscriptionFieldSnapshot<TSubscription> ToSnapshot() =>
        new(_accessor, new Dictionary<FilterIndexValue, SubscriptionEntry<TSubscription>[]>(_byValue));

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

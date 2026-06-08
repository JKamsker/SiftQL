namespace SiftQL.Index;

internal sealed class SubscriptionFieldSnapshot<TSubscription>
    where TSubscription : class
{
    private readonly Func<object, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, SubscriptionEntry<TSubscription>[]> _byValue;

    public SubscriptionFieldSnapshot(
        Func<object, FilterIndexValue?> accessor,
        Dictionary<FilterIndexValue, SubscriptionEntry<TSubscription>[]> byValue)
    {
        _accessor = accessor;
        _byValue = byValue;
    }

    public bool VisitCandidates<TState>(
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor,
        HashSet<TSubscription> seen)
    {
        if (!TryGetEntries(subject, out var items))
            return true;

        for (int i = 0; i < items.Length; i++)
        {
            TSubscription subscription = items[i].Subscription;
            if (seen.Add(subscription) && !visitor(subscription, ref state))
                return false;
        }

        return true;
    }

    public bool VisitMatches<TState>(
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor,
        HashSet<TSubscription> seen)
    {
        if (!TryGetEntries(subject, out var items))
            return true;

        for (int i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            if (entry.Matches(subject) &&
                seen.Add(entry.Subscription) &&
                !visitor(entry.Subscription, ref state))
            {
                return false;
            }
        }

        return true;
    }

    public void AddCandidates(
        object subject,
        List<TSubscription> candidates,
        HashSet<TSubscription> seen)
    {
        if (!TryGetEntries(subject, out var items))
            return;
        for (int i = 0; i < items.Length; i++)
        {
            TSubscription subscription = items[i].Subscription;
            if (seen.Add(subscription))
                candidates.Add(subscription);
        }
    }

    public void AddMatches(
        object subject,
        List<TSubscription> matches,
        HashSet<TSubscription> seen)
    {
        if (!TryGetEntries(subject, out var items))
            return;
        for (int i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            if (entry.Matches(subject) && seen.Add(entry.Subscription))
                matches.Add(entry.Subscription);
        }
    }

    private bool TryGetEntries(object subject, out SubscriptionEntry<TSubscription>[] items)
    {
        FilterIndexValue? value = _accessor(subject);
        if (value.HasValue && _byValue.TryGetValue(value.Value, out items!))
            return true;

        items = [];
        return false;
    }
}

namespace SiftQL.Index;

internal sealed class TypedSubscriptionFieldSnapshot<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly Func<TSubject, FilterIndexValue?> _accessor;
    private readonly Dictionary<FilterIndexValue, TypedSubscriptionEntry<TSubscription, TSubject>[]> _byValue;

    public TypedSubscriptionFieldSnapshot(
        Func<TSubject, FilterIndexValue?> accessor,
        Dictionary<FilterIndexValue, TypedSubscriptionEntry<TSubscription, TSubject>[]> byValue)
    {
        _accessor = accessor;
        _byValue = byValue;
    }

    public bool VisitCandidates<TState>(
        TSubject subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        if (!TryGetEntries(subject, out var items))
            return true;

        for (int i = 0; i < items.Length; i++)
        {
            if (!visitor(items[i].Subscription, ref state))
                return false;
        }

        return true;
    }

    public bool VisitMatches<TState>(
        TSubject subject,
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

    public void AddCandidates(TSubject subject, List<TSubscription> candidates)
    {
        if (!TryGetEntries(subject, out var items))
            return;
        for (int i = 0; i < items.Length; i++)
            candidates.Add(items[i].Subscription);
    }

    public void AddMatches(
        TSubject subject,
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

    private bool TryGetEntries(
        TSubject subject,
        out TypedSubscriptionEntry<TSubscription, TSubject>[] items)
    {
        FilterIndexValue? value = _accessor(subject);
        if (value.HasValue && _byValue.TryGetValue(value.Value, out items!))
            return true;

        items = [];
        return false;
    }
}

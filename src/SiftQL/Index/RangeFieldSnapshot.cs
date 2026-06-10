namespace SiftQL.Index;

// Immutable, lock-free queryable snapshot of a range field. Entries with a finite
// lower bound are sorted ascending by that bound (candidates = those with
// lower <= x); pure upper-bounded entries are sorted ascending by upper bound
// (candidates = those with upper >= x). Both are O(log n + k) and sound: the
// surfaced entries are a superset re-checked by the full predicate.
internal sealed class RangeFieldSnapshot<TSubscription>
    where TSubscription : class
{
    private readonly Func<object, decimal?> _accessor;
    private readonly decimal[] _lowerKeys;
    private readonly SubscriptionEntry<TSubscription>[] _lowerEntries;
    private readonly decimal[] _upperKeys;
    private readonly SubscriptionEntry<TSubscription>[] _upperEntries;

    public RangeFieldSnapshot(
        Func<object, decimal?> accessor,
        decimal[] lowerKeys,
        SubscriptionEntry<TSubscription>[] lowerEntries,
        decimal[] upperKeys,
        SubscriptionEntry<TSubscription>[] upperEntries)
    {
        _accessor = accessor;
        _lowerKeys = lowerKeys;
        _lowerEntries = lowerEntries;
        _upperKeys = upperKeys;
        _upperEntries = upperEntries;
    }

    public bool VisitCandidates<TState>(
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor,
        HashSet<TSubscription> seen)
    {
        if (_accessor(subject) is not { } x)
            return true;

        int lowerCount = RangeStab.CountLessOrEqual(_lowerKeys, x);
        for (int i = 0; i < lowerCount; i++)
        {
            TSubscription subscription = _lowerEntries[i].Subscription;
            if (seen.Add(subscription) && !visitor(subscription, ref state))
                return false;
        }

        int upperStart = RangeStab.FirstGreaterOrEqual(_upperKeys, x);
        for (int i = upperStart; i < _upperEntries.Length; i++)
        {
            TSubscription subscription = _upperEntries[i].Subscription;
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
        if (_accessor(subject) is not { } x)
            return true;

        int lowerCount = RangeStab.CountLessOrEqual(_lowerKeys, x);
        for (int i = 0; i < lowerCount; i++)
        {
            if (!VisitMatch(_lowerEntries[i], subject, ref state, visitor, seen))
                return false;
        }

        int upperStart = RangeStab.FirstGreaterOrEqual(_upperKeys, x);
        for (int i = upperStart; i < _upperEntries.Length; i++)
        {
            if (!VisitMatch(_upperEntries[i], subject, ref state, visitor, seen))
                return false;
        }

        return true;
    }

    public void AddCandidates(object subject, List<TSubscription> candidates, HashSet<TSubscription> seen)
    {
        if (_accessor(subject) is not { } x)
            return;

        int lowerCount = RangeStab.CountLessOrEqual(_lowerKeys, x);
        for (int i = 0; i < lowerCount; i++)
        {
            TSubscription subscription = _lowerEntries[i].Subscription;
            if (seen.Add(subscription))
                candidates.Add(subscription);
        }

        int upperStart = RangeStab.FirstGreaterOrEqual(_upperKeys, x);
        for (int i = upperStart; i < _upperEntries.Length; i++)
        {
            TSubscription subscription = _upperEntries[i].Subscription;
            if (seen.Add(subscription))
                candidates.Add(subscription);
        }
    }

    public void AddMatches(object subject, List<TSubscription> matches, HashSet<TSubscription> seen)
    {
        if (_accessor(subject) is not { } x)
            return;

        int lowerCount = RangeStab.CountLessOrEqual(_lowerKeys, x);
        for (int i = 0; i < lowerCount; i++)
        {
            var entry = _lowerEntries[i];
            if (entry.Matches(subject) && seen.Add(entry.Subscription))
                matches.Add(entry.Subscription);
        }

        int upperStart = RangeStab.FirstGreaterOrEqual(_upperKeys, x);
        for (int i = upperStart; i < _upperEntries.Length; i++)
        {
            var entry = _upperEntries[i];
            if (entry.Matches(subject) && seen.Add(entry.Subscription))
                matches.Add(entry.Subscription);
        }
    }

    private static bool VisitMatch<TState>(
        SubscriptionEntry<TSubscription> entry,
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor,
        HashSet<TSubscription> seen)
    {
        if (entry.Matches(subject) &&
            seen.Add(entry.Subscription) &&
            !visitor(entry.Subscription, ref state))
        {
            return false;
        }

        return true;
    }
}

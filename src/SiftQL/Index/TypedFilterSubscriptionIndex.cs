using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Index;

public sealed class TypedFilterSubscriptionIndex<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly object _sync = new();
    private readonly FilterSchema _schema = FilterSchema.For(typeof(TSubject));
    private readonly Dictionary<string, TypedSubscriptionFieldIndex<TSubscription, TSubject>> _fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TSubscription, List<TypedSubscriptionEntry<TSubscription, TSubject>>> _entries = [];
    private TypedSubscriptionEntry<TSubscription, TSubject>[] _unindexed = [];
    private int _count;
    private Snapshot _snapshot = new([], [], 0);

    public int Count => Volatile.Read(ref _snapshot).Count;

    public void Add(TSubscription subscription, FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        FilterExpression expression = filter ?? FilterExpression.Any;
        FilterIndexKey? key = FilterIndexExtractor.Extract(_schema, expression);
        var entry = new TypedSubscriptionEntry<TSubscription, TSubject>(
            subscription,
            key,
            FilterCompiler
                .Compile(typeof(TSubject), expression, FilterCompilerOptions.Immediate)
                .CreateMatcher<TSubject>());

        lock (_sync)
        {
            _count++;
            Track(entry);
            if (key is null)
                _unindexed = SubscriptionIndexArrays.Add(_unindexed, entry);
            else
                GetOrAddField(key).Add(key.Value, entry);
            PublishSnapshot();
        }
    }

    public void Remove(TSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_sync)
        {
            if (!_entries.TryGetValue(subscription, out var entries))
                return;

            var entry = SelectRemovalEntry(entries);
            bool removed = entry.Key is null
                ? TryRemoveUnindexed(entry)
                : TryRemoveIndexed(entry);
            if (!removed)
                return;

            Untrack(subscription, entries, entry);
            _count--;
            PublishSnapshot();
        }
    }

    public void ForEachCandidate<TState>(
        TSubject subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            if (!visitor(snapshot.Unindexed[i].Subscription, ref state))
                return;
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
        {
            if (!snapshot.Fields[i].VisitCandidates(subject, ref state, visitor))
                return;
        }
    }

    public void ForEachMatch<TState>(
        TSubject subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            var entry = snapshot.Unindexed[i];
            if (entry.Matches(subject) && !visitor(entry.Subscription, ref state))
                return;
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
        {
            if (!snapshot.Fields[i].VisitMatches(subject, ref state, visitor))
                return;
        }
    }

    public TSubscription[] SnapshotCandidates(TSubject subject)
    {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var candidates = new List<TSubscription>(snapshot.Unindexed.Length);
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
            candidates.Add(snapshot.Unindexed[i].Subscription);

        for (int i = 0; i < snapshot.Fields.Length; i++)
            snapshot.Fields[i].AddCandidates(subject, candidates);
        return candidates.ToArray();
    }

    public TSubscription[] SnapshotMatches(TSubject subject)
    {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var matches = new List<TSubscription>(snapshot.Unindexed.Length);
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            var entry = snapshot.Unindexed[i];
            if (entry.Matches(subject))
                matches.Add(entry.Subscription);
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
            snapshot.Fields[i].AddMatches(subject, matches);
        return matches.ToArray();
    }

    private TypedSubscriptionFieldIndex<TSubscription, TSubject> GetOrAddField(FilterIndexKey key)
    {
        if (_fields.TryGetValue(key.Field, out var existing))
            return existing;
        if (!_schema.TryGetField(key.Field, out FilterField? field))
            throw new FilterValidationException($"Filter field '{key.Field}' is not supported.");

        var created = new TypedSubscriptionFieldIndex<TSubscription, TSubject>(field);
        _fields.Add(key.Field, created);
        return created;
    }

    private void Track(TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        if (!_entries.TryGetValue(entry.Subscription, out var entries))
        {
            entries = [];
            _entries.Add(entry.Subscription, entries);
        }

        entries.Add(entry);
    }

    private void Untrack(
        TSubscription subscription,
        List<TypedSubscriptionEntry<TSubscription, TSubject>> entries,
        TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        entries.Remove(entry);
        if (entries.Count == 0)
            _entries.Remove(subscription);
    }

    private bool TryRemoveUnindexed(TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        var unindexed = SubscriptionIndexArrays.Remove(_unindexed, entry);
        if (unindexed is null)
            return false;

        _unindexed = unindexed;
        return true;
    }

    private bool TryRemoveIndexed(TypedSubscriptionEntry<TSubscription, TSubject> entry) =>
        entry.Key is { } key &&
        _fields.TryGetValue(key.Field, out var field) &&
        field.Remove(key.Value, entry);

    private void PublishSnapshot()
    {
        var fields = new TypedSubscriptionFieldSnapshot<TSubscription, TSubject>[_fields.Count];
        int index = 0;
        foreach (var field in _fields.Values)
            fields[index++] = field.ToSnapshot();
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed, fields, _count));
    }

    private static TypedSubscriptionEntry<TSubscription, TSubject> SelectRemovalEntry(
        IReadOnlyList<TypedSubscriptionEntry<TSubscription, TSubject>> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Key is null)
                return entries[i];
        }

        return entries[0];
    }

    private sealed record Snapshot(
        TypedSubscriptionEntry<TSubscription, TSubject>[] Unindexed,
        TypedSubscriptionFieldSnapshot<TSubscription, TSubject>[] Fields,
        int Count);
}

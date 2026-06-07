using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Index;

public delegate bool FilterCandidateVisitor<TSubscription, TState>(
    TSubscription subscription,
    ref TState state)
    where TSubscription : class;

public sealed class FilterSubscriptionIndex<TSubscription>
    where TSubscription : class
{
    private readonly object _sync = new();
    private readonly FilterSchema _schema;
    private readonly Dictionary<string, SubscriptionFieldIndex<TSubscription>> _fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TSubscription, List<SubscriptionEntry<TSubscription>>> _entries = [];
    private SubscriptionEntry<TSubscription>[] _unindexed = [];
    private int _count;
    private Snapshot _snapshot = new([], [], 0);

    public FilterSubscriptionIndex(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        _schema = FilterSchema.For(subjectType);
    }

    public int Count => Volatile.Read(ref _snapshot).Count;

    public void Add(TSubscription subscription, FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        FilterExpression expression = filter ?? FilterExpression.Any;
        FilterIndexKey? key = FilterIndexExtractor.Extract(_schema, expression);
        var entry = new SubscriptionEntry<TSubscription>(
            subscription,
            key,
            FilterCompiler.Compile(_schema.SubjectType, expression, FilterCompilerOptions.Immediate));

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
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(subject);
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
        object subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(visitor);
        if (!_schema.SubjectType.IsInstanceOfType(subject))
            return;

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

    public TSubscription[] SnapshotCandidates(object subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var candidates = new List<TSubscription>(snapshot.Unindexed.Length);
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
            candidates.Add(snapshot.Unindexed[i].Subscription);

        for (int i = 0; i < snapshot.Fields.Length; i++)
            snapshot.Fields[i].AddCandidates(subject, candidates);
        return candidates.ToArray();
    }

    public TSubscription[] SnapshotMatches(object subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!_schema.SubjectType.IsInstanceOfType(subject))
            return [];

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

    private SubscriptionFieldIndex<TSubscription> GetOrAddField(FilterIndexKey key)
    {
        if (_fields.TryGetValue(key.Field, out var existing))
            return existing;
        if (!_schema.TryGetField(key.Field, out FilterField? field))
            throw new FilterValidationException($"Filter field '{key.Field}' is not supported.");

        var created = new SubscriptionFieldIndex<TSubscription>(_schema.SubjectType, field);
        _fields.Add(key.Field, created);
        return created;
    }

    private void Track(SubscriptionEntry<TSubscription> entry)
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
        List<SubscriptionEntry<TSubscription>> entries,
        SubscriptionEntry<TSubscription> entry)
    {
        entries.Remove(entry);
        if (entries.Count == 0)
            _entries.Remove(subscription);
    }

    private bool TryRemoveUnindexed(SubscriptionEntry<TSubscription> entry)
    {
        var unindexed = SubscriptionIndexArrays.Remove(_unindexed, entry);
        if (unindexed is null)
            return false;

        _unindexed = unindexed;
        return true;
    }

    private bool TryRemoveIndexed(SubscriptionEntry<TSubscription> entry) =>
        entry.Key is { } key &&
        _fields.TryGetValue(key.Field, out var field) &&
        field.Remove(key.Value, entry);

    private void PublishSnapshot()
    {
        var fields = new SubscriptionFieldSnapshot<TSubscription>[_fields.Count];
        int index = 0;
        foreach (var field in _fields.Values)
            fields[index++] = field.ToSnapshot();
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed, fields, _count));
    }

    private static SubscriptionEntry<TSubscription> SelectRemovalEntry(
        IReadOnlyList<SubscriptionEntry<TSubscription>> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Key is null)
                return entries[i];
        }

        return entries[0];
    }

    private sealed record Snapshot(
        SubscriptionEntry<TSubscription>[] Unindexed,
        SubscriptionFieldSnapshot<TSubscription>[] Fields,
        int Count);
}

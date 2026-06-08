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
    private readonly SubscriptionBucket<SubscriptionEntry<TSubscription>> _unindexed = new();
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
                _unindexed.Add(entry);
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

            int removed = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                bool removedEntry = entry.Key is null
                    ? TryRemoveUnindexed(entry)
                    : TryRemoveIndexed(entry);
                if (!removedEntry)
                    continue;

                entries.RemoveAt(i);
                removed++;
            }

            if (removed == 0)
                return;

            if (entries.Count == 0)
                _entries.Remove(subscription);
            _count -= removed;
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
        var seen = new HashSet<TSubscription>();
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            TSubscription subscription = snapshot.Unindexed[i].Subscription;
            if (seen.Add(subscription) && !visitor(subscription, ref state))
                return;
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
        {
            if (!snapshot.Fields[i].VisitCandidates(subject, ref state, visitor, seen))
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
        var seen = new HashSet<TSubscription>();
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            var entry = snapshot.Unindexed[i];
            if (entry.Matches(subject) &&
                seen.Add(entry.Subscription) &&
                !visitor(entry.Subscription, ref state))
            {
                return;
            }
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
        {
            if (!snapshot.Fields[i].VisitMatches(subject, ref state, visitor, seen))
                return;
        }
    }

    public TSubscription[] SnapshotCandidates(object subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var candidates = new List<TSubscription>(snapshot.Unindexed.Length);
        var seen = new HashSet<TSubscription>();
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            TSubscription subscription = snapshot.Unindexed[i].Subscription;
            if (seen.Add(subscription))
                candidates.Add(subscription);
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
            snapshot.Fields[i].AddCandidates(subject, candidates, seen);
        return candidates.ToArray();
    }

    public TSubscription[] SnapshotMatches(object subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!_schema.SubjectType.IsInstanceOfType(subject))
            return [];

        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var matches = new List<TSubscription>(snapshot.Unindexed.Length);
        var seen = new HashSet<TSubscription>();
        for (int i = 0; i < snapshot.Unindexed.Length; i++)
        {
            var entry = snapshot.Unindexed[i];
            if (entry.Matches(subject) && seen.Add(entry.Subscription))
                matches.Add(entry.Subscription);
        }

        for (int i = 0; i < snapshot.Fields.Length; i++)
            snapshot.Fields[i].AddMatches(subject, matches, seen);
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

    private bool TryRemoveUnindexed(SubscriptionEntry<TSubscription> entry)
        => _unindexed.Remove(entry);

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
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed.Snapshot(), fields, _count));
    }

    private sealed record Snapshot(
        SubscriptionEntry<TSubscription>[] Unindexed,
        SubscriptionFieldSnapshot<TSubscription>[] Fields,
        int Count);
}

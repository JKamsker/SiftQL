using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Index;

public sealed class TypedFilterSubscriptionIndex<TSubscription, TSubject>
    where TSubscription : class
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TypedSubscriptionFieldIndex<TSubscription, TSubject>> _fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TSubscription, List<TypedSubscriptionEntry<TSubscription, TSubject>>> _entries = [];
    private readonly SubscriptionBucket<TypedSubscriptionEntry<TSubscription, TSubject>> _unindexed = new();
    private FilterSchema _schema;
    private int _schemaVersion;
    private int _count;
    private Snapshot _snapshot = new([], [], 0);

    public TypedFilterSubscriptionIndex()
    {
        FilterSchemaSnapshot snapshot = FilterSchemaSnapshot.For(typeof(TSubject));
        _schema = snapshot.Schema;
        _schemaVersion = snapshot.Version;
    }

    public int Count => Volatile.Read(ref _snapshot).Count;

    public void Add(TSubscription subscription, FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        FilterExpression expression = filter ?? FilterExpression.Any;

        lock (_sync)
        {
            EnsureCurrentSchemaLocked();
            AddEntry(CreateEntry(_schema, subscription, expression));
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
        TSubject subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(visitor);
        EnsureCurrentSchema();
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
        TSubject subject,
        ref TState state,
        FilterCandidateVisitor<TSubscription, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(visitor);
        EnsureCurrentSchema();
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

    public TSubscription[] SnapshotCandidates(TSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        EnsureCurrentSchema();
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

    public TSubscription[] SnapshotMatches(TSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        EnsureCurrentSchema();
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

    private static TypedSubscriptionEntry<TSubscription, TSubject> CreateEntry(
        FilterSchema schema,
        TSubscription subscription,
        FilterExpression expression)
    {
        FilterExpressionShapeValidator.Validate(expression);
        FilterExpression snapshot = FilterExpressionSnapshot.Clone(expression);
        FilterIndexKey? key = FilterIndexExtractor.Extract(schema, snapshot);
        return new TypedSubscriptionEntry<TSubscription, TSubject>(
            subscription,
            snapshot,
            key,
            FilterCompiler
                .Compile(schema.SubjectType, snapshot, FilterCompilerOptions.Immediate)
                .CreateMatcher<TSubject>());
    }

    private void AddEntry(TypedSubscriptionEntry<TSubscription, TSubject> entry)
    {
        _count++;
        Track(entry);
        if (entry.Key is not { } key)
            _unindexed.Add(entry);
        else
            GetOrAddField(key).Add(key.Value, entry);
    }

    private void EnsureCurrentSchema()
    {
        if (_schemaVersion == FilterSchema.Version)
            return;

        lock (_sync)
            EnsureCurrentSchemaLocked();
    }

    private void EnsureCurrentSchemaLocked()
    {
        if (_schemaVersion == FilterSchema.Version)
            return;

        FilterSchemaSnapshot current = FilterSchemaSnapshot.For(typeof(TSubject));
        if (_schemaVersion == current.Version)
            return;

        var existing = _entries.Values.SelectMany(static entries => entries).ToArray();
        var rebuilt = new TypedSubscriptionEntry<TSubscription, TSubject>[existing.Length];
        for (int i = 0; i < existing.Length; i++)
            rebuilt[i] = CreateEntry(current.Schema, existing[i].Subscription, existing[i].Expression);

        _schema = current.Schema;
        _schemaVersion = current.Version;
        _fields.Clear();
        _entries.Clear();
        _unindexed.Clear();
        _count = 0;
        for (int i = 0; i < rebuilt.Length; i++)
            AddEntry(rebuilt[i]);
        PublishSnapshot();
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

    private bool TryRemoveUnindexed(TypedSubscriptionEntry<TSubscription, TSubject> entry)
        => _unindexed.Remove(entry);

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
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed.Snapshot(), fields, _count));
    }

    private sealed record Snapshot(
        TypedSubscriptionEntry<TSubscription, TSubject>[] Unindexed,
        TypedSubscriptionFieldSnapshot<TSubscription, TSubject>[] Fields,
        int Count);
}

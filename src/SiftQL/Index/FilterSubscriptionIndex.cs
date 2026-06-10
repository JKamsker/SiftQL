using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
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
    private readonly Dictionary<string, SubscriptionFieldIndex<TSubscription>> _fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TSubscription, List<SubscriptionEntry<TSubscription>>> _entries = [];
    private readonly SubscriptionBucket<SubscriptionEntry<TSubscription>> _unindexed = new();
    private FilterSchema _schema;
    private int _schemaVersion;
    private int _count;
    private Snapshot _snapshot = new([], [], 0);

    public FilterSubscriptionIndex(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        FilterSchemaSnapshot snapshot = FilterSchemaSnapshot.For(subjectType);
        _schema = snapshot.Schema;
        _schemaVersion = snapshot.Version;
    }
    public int Count => Volatile.Read(ref _snapshot).Count;

    public FilterSubscriptionIndexStatistics GetStatistics()
    {
        EnsureCurrentSchema();
        lock (_sync)
        {
            var buckets = new Dictionary<string, int>(_fields.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _fields)
                buckets[pair.Key] = pair.Value.BucketCount;

            int unindexed = _unindexed.Count;
            return new FilterSubscriptionIndexStatistics(
                _count,
                _count - unindexed,
                unindexed,
                buckets);
        }
    }

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
                bool removedEntry = entry.Keys.Count == 0
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
        EnsureCurrentSchema();
        if (!_schema.SubjectType.IsInstanceOfType(subject))
            return;

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
        EnsureCurrentSchema();
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
        EnsureCurrentSchema();
        if (!_schema.SubjectType.IsInstanceOfType(subject))
            return [];

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
        EnsureCurrentSchema();
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
        FilterField field;
        if (_schema.SubjectType == typeof(ProjectedEvent))
            field = ProjectedEventFilterSchema.CreateField(key.Field);
        else if (!_schema.TryGetField(key.Field, out field!))
            throw new FilterValidationException($"Filter field '{key.Field}' is not supported.");

        var created = new SubscriptionFieldIndex<TSubscription>(_schema.SubjectType, field);
        _fields.Add(key.Field, created);
        return created;
    }

    private static SubscriptionEntry<TSubscription> CreateEntry(
        FilterSchema schema,
        TSubscription subscription,
        FilterExpression expression)
    {
        FilterExpressionShapeValidator.Validate(expression);
        FilterExpression snapshot = FilterExpressionSnapshot.Clone(expression);
        FilterSchema entrySchema = schema.SubjectType == typeof(ProjectedEvent)
            ? ProjectedEventFilterSchema.ForFilter(snapshot)
            : schema;
        IReadOnlyList<FilterIndexKey> keys = FilterIndexExtractor.ExtractKeys(entrySchema, snapshot);
        return new SubscriptionEntry<TSubscription>(
            subscription,
            snapshot,
            keys,
            entrySchema.SubjectType == typeof(ProjectedEvent)
                ? FilterCompiler.CompileWithSchema(
                    typeof(ProjectedEvent),
                    snapshot,
                    FilterCompilerOptions.Immediate,
                    errorFactory: null,
                    _ => entrySchema)
                : FilterCompiler.Compile(schema.SubjectType, snapshot, FilterCompilerOptions.Immediate));
    }

    private void AddEntry(SubscriptionEntry<TSubscription> entry)
    {
        _count++;
        Track(entry);
        if (entry.Keys.Count == 0)
        {
            _unindexed.Add(entry);
            return;
        }

        foreach (FilterIndexKey key in entry.Keys)
            GetOrAddField(key).Add(key.Value, entry);
    }

    private void EnsureCurrentSchema()
    {
        if (Volatile.Read(ref _schemaVersion) == FilterSchema.Version)
            return;

        lock (_sync)
            EnsureCurrentSchemaLocked();
    }

    private void EnsureCurrentSchemaLocked()
    {
        if (Volatile.Read(ref _schemaVersion) == FilterSchema.Version)
            return;

        FilterSchemaSnapshot current = FilterSchemaSnapshot.For(_schema.SubjectType);
        if (Volatile.Read(ref _schemaVersion) == current.Version)
            return;

        var existing = _entries.Values.SelectMany(static entries => entries).ToArray();
        var rebuilt = new SubscriptionEntry<TSubscription>[existing.Length];
        for (int i = 0; i < existing.Length; i++)
            rebuilt[i] = CreateEntry(current.Schema, existing[i].Subscription, existing[i].Expression);

        _schema = current.Schema;
        _fields.Clear();
        _entries.Clear();
        _unindexed.Clear();
        _count = 0;
        for (int i = 0; i < rebuilt.Length; i++)
            AddEntry(rebuilt[i]);
        PublishSnapshot();
        Volatile.Write(ref _schemaVersion, current.Version);
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

    private bool TryRemoveIndexed(SubscriptionEntry<TSubscription> entry)
    {
        bool removedAny = false;
        foreach (FilterIndexKey key in entry.Keys)
        {
            if (!_fields.TryGetValue(key.Field, out var field) ||
                !field.Remove(key.Value, entry))
            {
                continue;
            }

            removedAny = true;
            if (field.IsEmpty)
                _fields.Remove(key.Field);
        }

        return removedAny;
    }

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

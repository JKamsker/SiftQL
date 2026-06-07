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
    private readonly Dictionary<string, FieldIndex> _fields = new(StringComparer.OrdinalIgnoreCase);
    private TSubscription[] _unindexed = [];
    private Snapshot _snapshot = new([], [], 0);

    public int Count => Volatile.Read(ref _snapshot).Count;

    public void Add(TSubscription subscription, FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        FilterIndexKey? key = FilterIndexExtractor.Extract(_schema, filter ?? FilterExpression.Any);
        lock (_sync)
        {
            if (key is null)
                _unindexed = AddToArray(_unindexed, subscription);
            else
                GetOrAddField(key).Add(key.Value, subscription);
            PublishSnapshot();
        }
    }

    public void Remove(TSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_sync)
        {
            if (TryRemoveUnindexed(subscription) || TryRemoveIndexed(subscription))
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
            if (!visitor(snapshot.Unindexed[i], ref state))
                return;
        }

        FieldSnapshot[] fields = snapshot.Fields;
        for (int i = 0; i < fields.Length; i++)
        {
            if (!fields[i].VisitMatches(subject, ref state, visitor))
                return;
        }
    }

    public TSubscription[] SnapshotCandidates(TSubject subject)
    {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        var candidates = new List<TSubscription>(snapshot.Unindexed);
        FieldSnapshot[] fields = snapshot.Fields;
        for (int i = 0; i < fields.Length; i++)
            fields[i].AddMatches(subject, candidates);
        return candidates.ToArray();
    }

    private FieldIndex GetOrAddField(FilterIndexKey key)
    {
        if (_fields.TryGetValue(key.Field, out FieldIndex? existing))
            return existing;
        if (!_schema.TryGetField(key.Field, out FilterField? field))
            throw new FilterValidationException($"Filter field '{key.Field}' is not supported.");

        var created = new FieldIndex(field);
        _fields.Add(key.Field, created);
        return created;
    }

    private bool TryRemoveUnindexed(TSubscription subscription)
    {
        TSubscription[]? unindexed = RemoveFromArray(_unindexed, subscription);
        if (unindexed is null)
            return false;

        _unindexed = unindexed;
        return true;
    }

    private bool TryRemoveIndexed(TSubscription subscription)
    {
        foreach (FieldIndex field in _fields.Values)
        {
            if (field.Remove(subscription))
                return true;
        }

        return false;
    }

    private void PublishSnapshot()
    {
        var fields = new FieldSnapshot[_fields.Count];
        int index = 0;
        foreach (FieldIndex field in _fields.Values)
            fields[index++] = field.ToSnapshot();
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed, fields, _unindexed.Length + fields.Sum(static f => f.Count)));
    }

    private static TSubscription[] AddToArray(TSubscription[] items, TSubscription subscription)
    {
        var next = new TSubscription[items.Length + 1];
        Array.Copy(items, next, items.Length);
        next[^1] = subscription;
        return next;
    }

    private static TSubscription[]? RemoveFromArray(TSubscription[] items, TSubscription subscription)
    {
        int index = Array.IndexOf(items, subscription);
        if (index < 0)
            return null;
        if (items.Length == 1)
            return [];

        var next = new TSubscription[items.Length - 1];
        if (index > 0)
            Array.Copy(items, 0, next, 0, index);
        if (index < items.Length - 1)
            Array.Copy(items, index + 1, next, index, items.Length - index - 1);
        return next;
    }

    private sealed class FieldIndex
    {
        private readonly Func<TSubject, FilterIndexValue?> _accessor;
        private readonly Dictionary<FilterIndexValue, TSubscription[]> _byValue = [];

        public FieldIndex(FilterField field) =>
            _accessor = FilterIndexValueAccessor<TSubject>.Create(field);

        public void Add(FilterIndexValue value, TSubscription subscription) =>
            _byValue[value] = _byValue.TryGetValue(value, out TSubscription[]? items)
                ? AddToArray(items, subscription)
                : [subscription];

        public bool Remove(TSubscription subscription)
        {
            foreach (var pair in _byValue.ToArray())
            {
                TSubscription[]? items = RemoveFromArray(pair.Value, subscription);
                if (items is null)
                    continue;
                if (items.Length == 0)
                    _byValue.Remove(pair.Key);
                else
                    _byValue[pair.Key] = items;
                return true;
            }

            return false;
        }

        public FieldSnapshot ToSnapshot() =>
            new(
                _accessor,
                new Dictionary<FilterIndexValue, TSubscription[]>(_byValue));
    }

    private sealed class FieldSnapshot
    {
        private readonly Func<TSubject, FilterIndexValue?> _accessor;
        private readonly Dictionary<FilterIndexValue, TSubscription[]> _byValue;

        public FieldSnapshot(
            Func<TSubject, FilterIndexValue?> accessor,
            Dictionary<FilterIndexValue, TSubscription[]> byValue)
        {
            _accessor = accessor;
            _byValue = byValue;
            Count = byValue.Values.Sum(static items => items.Length);
        }

        public int Count { get; }

        public bool VisitMatches<TState>(
            TSubject subject,
            ref TState state,
            FilterCandidateVisitor<TSubscription, TState> visitor)
        {
            FilterIndexValue? value = _accessor(subject);
            if (!value.HasValue || !_byValue.TryGetValue(value.Value, out TSubscription[]? items))
                return true;

            for (int i = 0; i < items.Length; i++)
            {
                if (!visitor(items[i], ref state))
                    return false;
            }

            return true;
        }

        public void AddMatches(TSubject subject, List<TSubscription> candidates)
        {
            FilterIndexValue? value = _accessor(subject);
            if (!value.HasValue || !_byValue.TryGetValue(value.Value, out TSubscription[]? items))
                return;
            for (int i = 0; i < items.Length; i++)
                candidates.Add(items[i]);
        }
    }

    private sealed record Snapshot(
        TSubscription[] Unindexed,
        FieldSnapshot[] Fields,
        int Count);
}

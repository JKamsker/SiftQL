using System.Reflection;
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
    private TSubscription[] _unindexed = [];
    private readonly Dictionary<string, FieldIndex> _fields = new(StringComparer.OrdinalIgnoreCase);
    private int _count;
    private Snapshot _snapshot = new([], [], 0);

    public FilterSubscriptionIndex(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        _schema = FilterSchema.For(subjectType);
    }

    public int Count
    {
        get
        {
            return Volatile.Read(ref _snapshot).Count;
        }
    }

    public void Add(TSubscription subscription, FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        FilterIndexKey? key = FilterIndexExtractor.Extract(_schema, filter ?? FilterExpression.Any);
        lock (_sync)
        {
            _count++;
            if (key is null)
            {
                _unindexed = AddToArray(_unindexed, subscription);
                PublishSnapshot();
                return;
            }

            FieldIndex field = GetOrAddField(key);
            field.Add(key.Value, subscription);
            PublishSnapshot();
        }
    }

    public void Remove(TSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_sync)
        {
            TSubscription[]? unindexed = RemoveFromArray(_unindexed, subscription);
            if (unindexed is not null)
            {
                _unindexed = unindexed;
                _count--;
                PublishSnapshot();
                return;
            }

            foreach (FieldIndex field in _fields.Values)
            {
                if (field.Remove(subscription))
                {
                    _count--;
                    PublishSnapshot();
                    return;
                }
            }
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

    public TSubscription[] SnapshotCandidates(object subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
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

        var created = new FieldIndex(_schema.SubjectType, field);
        _fields.Add(key.Field, created);
        return created;
    }

    private void PublishSnapshot()
    {
        var fields = new FieldSnapshot[_fields.Count];
        int index = 0;
        foreach (FieldIndex field in _fields.Values)
            fields[index++] = field.ToSnapshot();
        Volatile.Write(ref _snapshot, new Snapshot(_unindexed, fields, _count));
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
        private readonly Func<object, FilterIndexValue?> _accessor;
        private readonly Dictionary<FilterIndexValue, TSubscription[]> _byValue = [];

        public FieldIndex(Type subjectType, FilterField field) =>
            _accessor = CreateAccessor(subjectType, field);

        public void Add(FilterIndexValue value, TSubscription subscription)
        {
            _byValue[value] = _byValue.TryGetValue(value, out TSubscription[]? items)
                ? AddToArray(items, subscription)
                : [subscription];
        }

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
            new(_accessor, new Dictionary<FilterIndexValue, TSubscription[]>(_byValue));
    }

    private sealed class FieldSnapshot
    {
        private readonly Func<object, FilterIndexValue?> _accessor;
        private readonly Dictionary<FilterIndexValue, TSubscription[]> _byValue;

        public FieldSnapshot(
            Func<object, FilterIndexValue?> accessor,
            Dictionary<FilterIndexValue, TSubscription[]> byValue)
        {
            _accessor = accessor;
            _byValue = byValue;
        }

        public bool VisitMatches<TState>(
            object subject,
            ref TState state,
            FilterCandidateVisitor<TSubscription, TState> visitor)
        {
            if (!TryCreateActual(subject, out FilterIndexValue value))
                return true;
            if (_byValue.TryGetValue(value, out TSubscription[]? items))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (!visitor(items[i], ref state))
                        return false;
                }
            }

            return true;
        }

        public void AddMatches(object subject, List<TSubscription> candidates)
        {
            if (!TryCreateActual(subject, out FilterIndexValue value))
                return;
            if (_byValue.TryGetValue(value, out TSubscription[]? items))
            {
                for (int i = 0; i < items.Length; i++)
                    candidates.Add(items[i]);
            }
        }

        private bool TryCreateActual(object subject, out FilterIndexValue value)
        {
            FilterIndexValue? actual = _accessor(subject);
            if (actual.HasValue)
            {
                value = actual.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    private static Func<object, FilterIndexValue?> CreateAccessor(Type subjectType, FilterField field)
    {
        MethodInfo method = typeof(FilterSubscriptionIndex<TSubscription>)
            .GetMethod(nameof(CreateTypedAccessor), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(subjectType);
        return (Func<object, FilterIndexValue?>)method.Invoke(null, [field])!;
    }

    private static Func<object, FilterIndexValue?> CreateTypedAccessor<TSubject>(FilterField field)
    {
        Func<TSubject, FilterIndexValue?> accessor = FilterIndexValueAccessor<TSubject>.Create(field);
        return subject => subject is TSubject typed ? accessor(typed) : null;
    }

    private sealed record Snapshot(
        TSubscription[] Unindexed,
        FieldSnapshot[] Fields,
        int Count);
}

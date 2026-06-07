using SiftQL;

namespace SiftQL.Projection;

public readonly record struct ProjectionDispatchGroup<TProjection>(
    SubscriptionIdBatch SubscriptionIds,
    TProjection Projection)
    where TProjection : class;

// Mutable value accumulator: keep it in a local or ref-passed state field so
// Add calls mutate the same instance instead of a property copy.
public struct ProjectionMatchAccumulator<TProjection>
    where TProjection : class
{
    private int _matchCount;
    private int _groupCount;
    private Group _first;
    private Group _second;
    private Group _third;
    private Group _fourth;
    private Dictionary<string, Group>? _overflowGroups;

    public bool IsEmpty => _matchCount == 0;

    public int GroupCount => _groupCount;

    public void Add(
        string subscriptionId,
        string projectionKey,
        TProjection projection)
    {
        if (_groupCount == 0)
            AddFirstGroup(subscriptionId, projectionKey, projection);
        else if (!TryAddToExistingGroup(subscriptionId, projectionKey))
            AddNewGroup(subscriptionId, projectionKey, projection);

        _matchCount++;
    }

    public Enumerator GetEnumerator() =>
        new(_groupCount, _first, _second, _third, _fourth, _overflowGroups);

    public ProjectionDispatchGroup<TProjection>[] ToArray()
    {
        if (_groupCount == 0)
            return [];

        var result = new ProjectionDispatchGroup<TProjection>[_groupCount];
        int index = 0;
        int inlineCount = InlineGroupCount;
        for (; index < inlineCount; index++)
            result[index] = GetInlineGroup(index).ToDispatchGroup();
        if (_overflowGroups is null)
            return result;

        foreach (Group group in _overflowGroups.Values)
            result[index++] = group.ToDispatchGroup();
        return result;
    }

    private int InlineGroupCount => Math.Min(_groupCount, 4);

    private void AddFirstGroup(
        string subscriptionId,
        string projectionKey,
        TProjection projection)
    {
        _first = new Group(subscriptionId, projectionKey, projection);
        _groupCount = 1;
    }

    private bool TryAddToExistingGroup(string subscriptionId, string projectionKey)
    {
        if (_first.Matches(projectionKey))
        {
            _first.Add(subscriptionId);
            return true;
        }

        if (_groupCount > 1 && _second.Matches(projectionKey))
        {
            _second.Add(subscriptionId);
            return true;
        }

        if (_groupCount > 2 && _third.Matches(projectionKey))
        {
            _third.Add(subscriptionId);
            return true;
        }

        if (_groupCount > 3 && _fourth.Matches(projectionKey))
        {
            _fourth.Add(subscriptionId);
            return true;
        }

        if (_overflowGroups is null ||
            !_overflowGroups.TryGetValue(projectionKey, out Group group))
        {
            return false;
        }

        group.Add(subscriptionId);
        _overflowGroups[projectionKey] = group;
        return true;
    }

    private void AddNewGroup(
        string subscriptionId,
        string projectionKey,
        TProjection projection)
    {
        var group = new Group(subscriptionId, projectionKey, projection);
        switch (_groupCount)
        {
            case 1:
                _second = group;
                break;
            case 2:
                _third = group;
                break;
            case 3:
                _fourth = group;
                break;
            default:
                (_overflowGroups ??= new Dictionary<string, Group>(4, StringComparer.Ordinal))
                    .Add(projectionKey, group);
                break;
        }

        _groupCount++;
    }

    private Group GetInlineGroup(int index) =>
        index switch
        {
            0 => _first,
            1 => _second,
            2 => _third,
            _ => _fourth,
        };

    public struct Enumerator
    {
        private readonly int _groupCount;
        private readonly Group _first;
        private readonly Group _second;
        private readonly Group _third;
        private readonly Group _fourth;
        private readonly Dictionary<string, Group>? _overflowGroups;
        private Dictionary<string, Group>.ValueCollection.Enumerator _groupEnumerator;
        private int _index;

        internal Enumerator(
            int groupCount,
            Group first,
            Group second,
            Group third,
            Group fourth,
            Dictionary<string, Group>? overflowGroups)
        {
            _groupCount = groupCount;
            _first = first;
            _second = second;
            _third = third;
            _fourth = fourth;
            _overflowGroups = overflowGroups;
            _groupEnumerator = overflowGroups?.Values.GetEnumerator() ?? default;
            _index = 0;
            Current = default;
        }

        public ProjectionDispatchGroup<TProjection> Current { get; private set; }

        public bool MoveNext()
        {
            if (_index < Math.Min(_groupCount, 4))
            {
                Current = GetCurrentInlineGroup().ToDispatchGroup();
                _index++;
                return true;
            }

            if (_overflowGroups is null ||
                !_groupEnumerator.MoveNext())
            {
                return false;
            }

            _index++;
            Current = _groupEnumerator.Current.ToDispatchGroup();
            return true;
        }

        private Group GetCurrentInlineGroup() =>
            _index switch
            {
                0 => _first,
                1 => _second,
                2 => _third,
                _ => _fourth,
            };
    }

    internal struct Group
    {
        private readonly string? _projectionKey;
        private string? _firstId;
        private string? _secondId;
        private string? _thirdId;
        private string? _fourthId;
        private List<string>? _extraIds;
        private int _count;
        private readonly TProjection? _projection;

        public Group(string subscriptionId, string projectionKey, TProjection projection)
        {
            _projectionKey = projectionKey;
            _firstId = subscriptionId;
            _secondId = null;
            _thirdId = null;
            _fourthId = null;
            _extraIds = null;
            _count = 1;
            _projection = projection;
        }

        public bool Matches(string projectionKey) =>
            string.Equals(_projectionKey, projectionKey, StringComparison.Ordinal);

        public void Add(string subscriptionId)
        {
            switch (_count)
            {
                case 1:
                    _secondId = subscriptionId;
                    break;
                case 2:
                    _thirdId = subscriptionId;
                    break;
                case 3:
                    _fourthId = subscriptionId;
                    break;
                default:
                    (_extraIds ??= new List<string>(2)).Add(subscriptionId);
                    break;
            }

            _count++;
        }

        public ProjectionDispatchGroup<TProjection> ToDispatchGroup() =>
            new(ToSubscriptionIds(), _projection!);

        private SubscriptionIdBatch ToSubscriptionIds() =>
            new(
                _count,
                _firstId,
                _secondId,
                _thirdId,
                _fourthId,
                _extraIds?.ToArray());
    }
}

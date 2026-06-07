namespace SiftQL.Index;

internal sealed class SubscriptionBucket<TEntry>
{
    private readonly List<TEntry> _items = [];
    private TEntry[] _snapshot = [];
    private bool _dirty;

    public int Count => _items.Count;

    public void Add(TEntry entry)
    {
        _items.Add(entry);
        _dirty = true;
    }

    public bool Remove(TEntry entry)
    {
        int index = _items.IndexOf(entry);
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        _dirty = true;
        return true;
    }

    public TEntry[] Snapshot()
    {
        if (_dirty)
        {
            _snapshot = _items.Count == 0 ? [] : _items.ToArray();
            _dirty = false;
        }

        return _snapshot;
    }
}

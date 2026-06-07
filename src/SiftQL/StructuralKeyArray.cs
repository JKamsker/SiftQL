namespace SiftQL;

internal readonly struct StructuralKeyArray<T> : IEquatable<StructuralKeyArray<T>>
{
    private readonly T[]? _items;

    public StructuralKeyArray(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    private StructuralKeyArray(T[] items) => _items = items;

    public static StructuralKeyArray<T> Empty { get; } = new(Array.Empty<T>());

    public static StructuralKeyArray<T> From<TSource>(
        IReadOnlyList<TSource> items,
        Func<TSource, T> selector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selector);
        if (items.Count == 0)
            return Empty;

        var mapped = new T[items.Count];
        for (int i = 0; i < mapped.Length; i++)
            mapped[i] = selector(items[i]);
        return new StructuralKeyArray<T>(mapped);
    }

    public static StructuralKeyArray<T> From<TSource, TState>(
        IReadOnlyList<TSource> items,
        TState state,
        Func<TSource, TState, T> selector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selector);
        if (items.Count == 0)
            return Empty;

        var mapped = new T[items.Count];
        for (int i = 0; i < mapped.Length; i++)
            mapped[i] = selector(items[i], state);
        return new StructuralKeyArray<T>(mapped);
    }

    public int Count => Items.Length;
    public T this[int index] => Items[index];

    private T[] Items => _items ?? Array.Empty<T>();

    public bool Equals(StructuralKeyArray<T> other)
    {
        T[] left = Items;
        T[] right = other.Items;
        if (left.Length != right.Length)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is StructuralKeyArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        T[] items = Items;
        hash.Add(items.Length);
        for (int i = 0; i < items.Length; i++)
            hash.Add(items[i]);
        return hash.ToHashCode();
    }
}

using System.Collections;
using System.Collections.Immutable;

namespace SiftQL.Generators;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items.IsDefault ? ImmutableArray<T>.Empty : items;

    public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

    public int Count => Items.Length;
    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;
    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        ImmutableArray<T> left = Items;
        ImmutableArray<T> right = other.Items;
        if (left.Length != right.Length)
            return false;

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        ImmutableArray<T> items = Items;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < items.Length; i++)
            hash = unchecked(hash * 31 + comparer.GetHashCode(items[i]!));
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);
}

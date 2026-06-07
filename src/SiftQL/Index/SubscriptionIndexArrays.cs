namespace SiftQL.Index;

internal static class SubscriptionIndexArrays
{
    public static TItem[] Add<TItem>(TItem[] items, TItem item)
    {
        var next = new TItem[items.Length + 1];
        Array.Copy(items, next, items.Length);
        next[^1] = item;
        return next;
    }

    public static TItem[]? Remove<TItem>(TItem[] items, TItem item)
    {
        int index = Array.IndexOf(items, item);
        if (index < 0)
            return null;
        if (items.Length == 1)
            return [];

        var next = new TItem[items.Length - 1];
        if (index > 0)
            Array.Copy(items, 0, next, 0, index);
        if (index < items.Length - 1)
            Array.Copy(items, index + 1, next, index, items.Length - index - 1);
        return next;
    }
}

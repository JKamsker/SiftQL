using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Translation;

namespace SiftQL;

public readonly record struct SubscriptionIdBatch(
    int Count,
    string? First,
    string? Second = null,
    string? Third = null,
    string? Fourth = null,
    string[]? Overflow = null)
{
    public static SubscriptionIdBatch One(string subscriptionId) =>
        new(1, subscriptionId);

    public string this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return index switch
            {
                0 => First!,
                1 => Second!,
                2 => Third!,
                3 => Fourth!,
                _ => Overflow is { } overflow
                    ? overflow[index - 4]
                    : throw new InvalidOperationException("Subscription id overflow is missing."),
            };
        }
    }

    public string[] ToArray()
    {
        var ids = new string[Count];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = this[i];
        return ids;
    }
}

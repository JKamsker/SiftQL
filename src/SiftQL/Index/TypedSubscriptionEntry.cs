using SiftQL.Kernel;
using SiftQL.Expressions;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionEntry<TSubscription, TSubject>(
    TSubscription subscription,
    FilterExpression expression,
    IReadOnlyList<FilterIndexKey> keys,
    RangeCondition? rangeKey,
    CompiledKernelMatcher<TSubject> matcher)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterExpression Expression { get; } = expression;

    // A single entry may be registered under multiple equality buckets (e.g. one
    // per value of an In filter, or one per branch of an Or). Empty = not in the
    // equality index.
    public IReadOnlyList<FilterIndexKey> Keys { get; } = keys;

    // Set when the entry is placed in a range field index instead. Mutually
    // exclusive with Keys; both empty/null = unindexed.
    public RangeCondition? RangeKey { get; } = rangeKey;

    public bool Matches(TSubject subject) => matcher.Matches(subject);
}

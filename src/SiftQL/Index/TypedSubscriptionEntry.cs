using SiftQL.Kernel;
using SiftQL.Expressions;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionEntry<TSubscription, TSubject>(
    TSubscription subscription,
    FilterExpression expression,
    IReadOnlyList<FilterIndexKey> keys,
    CompiledKernelMatcher<TSubject> matcher)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterExpression Expression { get; } = expression;

    // A single entry may be registered under multiple index buckets (e.g. one
    // per value of an In filter, or one per branch of an Or). Empty = unindexed.
    public IReadOnlyList<FilterIndexKey> Keys { get; } = keys;

    public bool Matches(TSubject subject) => matcher.Matches(subject);
}

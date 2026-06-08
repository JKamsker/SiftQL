using SiftQL.Kernel;
using SiftQL.Expressions;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionEntry<TSubscription, TSubject>(
    TSubscription subscription,
    FilterExpression expression,
    FilterIndexKey? key,
    CompiledKernelMatcher<TSubject> matcher)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterExpression Expression { get; } = expression;
    public FilterIndexKey? Key { get; } = key;

    public bool Matches(TSubject subject) => matcher.Matches(subject);
}

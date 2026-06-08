using SiftQL.Kernel;
using SiftQL.Expressions;

namespace SiftQL.Index;

internal sealed class SubscriptionEntry<TSubscription>(
    TSubscription subscription,
    FilterExpression expression,
    FilterIndexKey? key,
    CompiledKernel kernel)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterExpression Expression { get; } = expression;
    public FilterIndexKey? Key { get; } = key;

    public bool Matches(object subject) => kernel.Matches(subject);
}

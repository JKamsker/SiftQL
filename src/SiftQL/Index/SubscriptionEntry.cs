using SiftQL.Kernel;

namespace SiftQL.Index;

internal sealed class SubscriptionEntry<TSubscription>(
    TSubscription subscription,
    FilterIndexKey? key,
    CompiledKernel kernel)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterIndexKey? Key { get; } = key;

    public bool Matches(object subject) => kernel.Matches(subject);
}

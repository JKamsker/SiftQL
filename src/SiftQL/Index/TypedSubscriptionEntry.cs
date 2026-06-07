using SiftQL.Kernel;

namespace SiftQL.Index;

internal sealed class TypedSubscriptionEntry<TSubscription, TSubject>(
    TSubscription subscription,
    FilterIndexKey? key,
    CompiledKernelMatcher<TSubject> matcher)
    where TSubscription : class
{
    public TSubscription Subscription { get; } = subscription;
    public FilterIndexKey? Key { get; } = key;

    public bool Matches(TSubject subject) => matcher.Matches(subject);
}

// removed: game-specific events
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class DispatchScanCase : IBenchmarkCase
{
    private const int SubscriptionCount = 256;
    private const int EventMask = 1023;
    private readonly ItemUsedEvent[] _events = DispatchEvents.Create(EventMask, SubscriptionCount);
    private readonly Dictionary<int, DispatchSubscription[]> _manual;
    private readonly DispatchSubscription[] _subscriptions;

    public DispatchScanCase()
    {
        _subscriptions = DispatchSubscriptions.Create(SubscriptionCount);
        _manual = DispatchSubscriptions.ByItemId(_subscriptions);
    }

    public string Category => "Dispatch";
    public string Name => "256 exact scalar scan";
    public int Iterations => 250_000;

    public void Manual(int iterations)
    {
        var items = _events;
        var byItemId = _manual;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & EventMask];
            if (byItemId.TryGetValue(item.ItemId, out DispatchSubscription[]? candidates))
                matches += candidates.Length;
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _events;
        var subscriptions = _subscriptions;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & EventMask];
            for (int candidate = 0; candidate < subscriptions.Length; candidate++)
            {
                if (subscriptions[candidate].Matcher.Matches(item))
                    matches++;
            }
        }

        BenchmarkSink.Consume(matches);
    }
}

internal sealed class DispatchIndexCase : IBenchmarkCase
{
    private const int SubscriptionCount = 256;
    private const int EventMask = 1023;
    private static readonly FilterCandidateVisitor<DispatchSubscription, DispatchMatchState> s_visitCandidate =
        VisitCandidate;
    private readonly ItemUsedEvent[] _events = DispatchEvents.Create(EventMask, SubscriptionCount);
    private readonly Dictionary<int, DispatchSubscription[]> _manual;
    private readonly TypedFilterSubscriptionIndex<DispatchSubscription, ItemUsedEvent> _index = new();

    public DispatchIndexCase()
    {
        _manual = DispatchSubscriptions.ByItemId(DispatchSubscriptions.Create(SubscriptionCount));
        foreach (DispatchSubscription subscription in _manual.Values.SelectMany(static item => item))
        {
            _index.Add(subscription, DispatchSubscriptions.Filter(subscription.ItemId));
        }
    }

    public string Category => "Dispatch";
    public string Name => "256 exact scalar subscriptions";
    public int Iterations => 1_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        var byItemId = _manual;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & EventMask];
            if (byItemId.TryGetValue(item.ItemId, out DispatchSubscription[]? candidates))
                matches += candidates.Length;
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _events;
        var index = _index;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & EventMask];
            var state = new DispatchMatchState(item);
            index.ForEachCandidate(item, ref state, s_visitCandidate);
            matches += state.Matches;
        }

        BenchmarkSink.Consume(matches);
    }

    private static bool VisitCandidate(DispatchSubscription subscription, ref DispatchMatchState state)
    {
        if (subscription.Matcher.Matches(state.Subject))
            state.Matches++;
        return true;
    }
}

internal struct DispatchMatchState
{
    public DispatchMatchState(ItemUsedEvent subject)
    {
        Subject = subject;
        Matches = 0;
    }

    public ItemUsedEvent Subject { get; }
    public long Matches { get; set; }
}

internal sealed record DispatchSubscription(
    int ItemId,
    string Id,
    CompiledKernel Kernel,
    CompiledKernelMatcher<ItemUsedEvent> Matcher);

internal static class DispatchEvents
{
    public static ItemUsedEvent[] Create(int eventMask, int subscriptionCount) =>
        Enumerable.Range(0, eventMask + 1)
            .Select(index => new ItemUsedEvent(
                Guid.NewGuid(),
                CharacterId: 10 + index,
                MapId: 1,
                ItemId: index % subscriptionCount,
                ItemName: "Item",
                Quantity: 1))
            .ToArray();
}

internal static class DispatchSubscriptions
{
    public static DispatchSubscription[] Create(int count) =>
        Enumerable.Range(0, count)
            .Select(static itemId =>
            {
                var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), Filter(itemId));
                return new DispatchSubscription(
                    itemId,
                    "item-" + itemId,
                    kernel,
                    kernel.CreateMatcher<ItemUsedEvent>());
            })
            .ToArray();

    public static Dictionary<int, DispatchSubscription[]> ByItemId(DispatchSubscription[] subscriptions) =>
        subscriptions.ToDictionary(static item => item.ItemId, static item => new[] { item });

    public static FilterExpression Filter(int itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId));
}

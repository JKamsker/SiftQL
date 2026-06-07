// removed: game-specific events
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Benchmarks;

internal sealed class ProjectionGroupingCase : IBenchmarkCase
{
    private readonly ProjectionGroupingSubscription[] _subscriptions = CreateSubscriptions();

    public string Category => "Projection";
    public string Name => "group 16 projected matches";
    public int Iterations => 500_000;

    public void Manual(int iterations)
    {
        ProjectionGroupingSubscription[] subscriptions = _subscriptions;
        long totalIds = 0;
        for (int i = 0; i < iterations; i++)
        {
            var groups = subscriptions
                .GroupBy(static subscription => subscription.Projection.Key, StringComparer.Ordinal)
                .Select(static group => new ProjectionDispatchGroup<CompiledProjection<object>>(
                    ToBatch(group.Select(static subscription => subscription.Id).ToArray()),
                    group.First().Projection))
                .ToArray();
            for (int group = 0; group < groups.Length; group++)
                totalIds += groups[group].SubscriptionIds.Count;
        }

        BenchmarkSink.Consume(totalIds);
    }

    public void Engine(int iterations)
    {
        ProjectionGroupingSubscription[] subscriptions = _subscriptions;
        long totalIds = 0;
        for (int i = 0; i < iterations; i++)
        {
            var groups = new ProjectionMatchAccumulator<CompiledProjection<object>>();
            for (int item = 0; item < subscriptions.Length; item++)
            {
                ProjectionGroupingSubscription subscription = subscriptions[item];
                groups.Add(
                    subscription.Id,
                    subscription.Projection.Key,
                    subscription.Projection);
            }

            foreach (var group in groups)
                totalIds += group.SubscriptionIds.Count;
        }

        BenchmarkSink.Consume(totalIds);
    }

    private static ProjectionGroupingSubscription[] CreateSubscriptions()
    {
        CompiledProjection<object>[] projections =
        [
            Compile(nameof(ItemUsedEvent.ItemId)),
            Compile(nameof(ItemUsedEvent.Quantity)),
            Compile(nameof(ItemUsedEvent.CharacterId)),
            Compile(nameof(ItemUsedEvent.EventId)),
        ];

        var subscriptions = new ProjectionGroupingSubscription[16];
        for (int i = 0; i < subscriptions.Length; i++)
        {
            subscriptions[i] = new ProjectionGroupingSubscription(
                "projected-" + i,
                projections[i & 3]);
        }

        return subscriptions;
    }

    private static CompiledProjection<object> Compile(string field) =>
        ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(field),
            RejectInclude);

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static SubscriptionIdBatch ToBatch(string[] ids) =>
        new(
            ids.Length,
            ids.Length > 0 ? ids[0] : null,
            ids.Length > 1 ? ids[1] : null,
            ids.Length > 2 ? ids[2] : null,
            ids.Length > 3 ? ids[3] : null,
            ids.Length > 4 ? ids[4..] : null);

    private sealed record ProjectionGroupingSubscription(
        string Id,
        CompiledProjection<object> Projection);
}

// removed: game-specific events
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Benchmarks;

internal sealed class ServerProjectedDispatchPipelineCase : IBenchmarkCase
{
    private const int SubscriptionsPerProjection = 4;
    private static readonly string s_eventType = typeof(ItemUsedEvent).FullName ?? nameof(ItemUsedEvent);
    private static readonly FilterCandidateVisitor<ProjectedDispatchSubscription<ItemUsedEvent>, ProjectedDispatchState<ItemUsedEvent>> s_visit =
        VisitCandidate;
    private readonly object _context = new();
    private readonly ItemUsedEvent _event = new(Guid.NewGuid(), 10, 1, 100, "Potion", 2);
    private readonly TypedFilterSubscriptionIndex<ProjectedDispatchSubscription<ItemUsedEvent>, ItemUsedEvent> _index = new();
    private readonly SubscriptionIdBatch[] _manualIds;

    public ServerProjectedDispatchPipelineCase()
    {
        string[] fields =
        [
            nameof(ItemUsedEvent.ItemId),
            nameof(ItemUsedEvent.Quantity),
            nameof(ItemUsedEvent.CharacterId),
            nameof(ItemUsedEvent.EventId),
        ];
        _manualIds = ProjectedDispatchBenchmarks.CreateIdGroups("server", fields.Length, SubscriptionsPerProjection);
        AddSubscriptions(fields, nameof(ItemUsedEvent.ItemId), 100);
    }

    public string Category => "Dispatch";
    public string Name => "server projected pipeline";
    public int Iterations => 100_000;

    public void Manual(int iterations)
    {
        var item = _event;
        SubscriptionIdBatch[] groups = _manualIds;
        long sent = 0;
        for (int i = 0; i < iterations; i++)
        {
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[0], s_eventType, ServerProjection(item, 0));
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[1], s_eventType, ServerProjection(item, 1));
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[2], s_eventType, ServerProjection(item, 2));
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[3], s_eventType, ServerProjection(item, 3));
        }

        BenchmarkSink.Consume(sent);
    }

    public void Engine(int iterations)
    {
        var subject = _event;
        var index = _index;
        object context = _context;
        long sent = 0;
        for (int i = 0; i < iterations; i++)
        {
            var state = new ProjectedDispatchState<ItemUsedEvent>(subject);
            index.ForEachCandidate(subject, ref state, s_visit);
            foreach (var match in state.Matches)
            {
                var payload = match.Projection.ProjectPayloadAsync(
                        subject,
                        context,
                        MessagePack.MessagePackSerializerOptions.Standard,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                sent += ProjectedDispatchBenchmarks.Dispatch(match.SubscriptionIds, s_eventType, payload);
            }
        }

        BenchmarkSink.Consume(sent);
    }

    private void AddSubscriptions(string[] fields, string filterField, int value)
    {
        var filter = ProjectedDispatchBenchmarks.Equal(filterField, value);
        for (int projection = 0; projection < fields.Length; projection++)
        {
            var compiled = ProjectedDispatchBenchmarks.Compile<ItemUsedEvent>(fields[projection]);
            for (int id = 0; id < SubscriptionsPerProjection; id++)
            {
                var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter);
                _index.Add(new ProjectedDispatchSubscription<ItemUsedEvent>(
                    _manualIds[projection][id],
                    kernel,
                    kernel.CreateMatcher<ItemUsedEvent>(),
                    compiled), filter);
            }
        }
    }

    private static bool VisitCandidate(
        ProjectedDispatchSubscription<ItemUsedEvent> subscription,
        ref ProjectedDispatchState<ItemUsedEvent> state)
    {
        if (subscription.Matcher.Matches(state.Subject))
            state.Matches.Add(subscription.Id, subscription.Projection.Key, subscription.Projection);
        return true;
    }

    private static ProjectedEvent ServerProjection(ItemUsedEvent item, int projection) =>
        ProjectedDispatchBenchmarks.OneField(
            s_eventType,
            nameof(ItemUsedEvent),
            projection switch
            {
                0 => ProjectedValues.Field(nameof(ItemUsedEvent.ItemId), ProjectedValues.Integer(item.ItemId)),
                1 => ProjectedValues.Field(nameof(ItemUsedEvent.Quantity), ProjectedValues.Integer(item.Quantity)),
                2 => ProjectedValues.Field(nameof(ItemUsedEvent.CharacterId), ProjectedValues.Integer(item.CharacterId)),
                _ => ProjectedValues.Field(nameof(ItemUsedEvent.EventId), ProjectedValues.Guid(item.EventId)),
            });
}

internal sealed class ClientProjectedDispatchPipelineCase : IBenchmarkCase
{
    private const int SubscriptionsPerProjection = 4;
    private static readonly string s_eventType = typeof(UiSelectionChangedEvent).FullName ?? nameof(UiSelectionChangedEvent);
    private static readonly FilterCandidateVisitor<ProjectedDispatchSubscription<UiSelectionChangedEvent>, ProjectedDispatchState<UiSelectionChangedEvent>> s_visit =
        VisitCandidate;
    private readonly object _context = new();
    private readonly UiSelectionChangedEvent _event = new(Guid.NewGuid(), 10, "inventory", "slot-grid");
    private readonly TypedFilterSubscriptionIndex<ProjectedDispatchSubscription<UiSelectionChangedEvent>, UiSelectionChangedEvent> _index = new();
    private readonly SubscriptionIdBatch[] _manualIds;

    public ClientProjectedDispatchPipelineCase()
    {
        string[] fields =
        [
            nameof(UiSelectionChangedEvent.ElementId),
            nameof(UiSelectionChangedEvent.SelectedValue),
            nameof(UiSelectionChangedEvent.CharacterId),
        ];
        _manualIds = ProjectedDispatchBenchmarks.CreateIdGroups("client", fields.Length, SubscriptionsPerProjection);
        AddSubscriptions(fields);
    }

    public string Category => "Dispatch";
    public string Name => "client projected pipeline";
    public int Iterations => 100_000;

    public void Manual(int iterations)
    {
        var item = _event;
        SubscriptionIdBatch[] groups = _manualIds;
        long sent = 0;
        for (int i = 0; i < iterations; i++)
        {
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[0], s_eventType, ClientProjection(item, 0));
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[1], s_eventType, ClientProjection(item, 1));
            sent += ProjectedDispatchBenchmarks.Dispatch(groups[2], s_eventType, ClientProjection(item, 2));
        }

        BenchmarkSink.Consume(sent);
    }

    public void Engine(int iterations)
    {
        var subject = _event;
        var index = _index;
        object context = _context;
        long sent = 0;
        for (int i = 0; i < iterations; i++)
        {
            var state = new ProjectedDispatchState<UiSelectionChangedEvent>(subject);
            index.ForEachCandidate(subject, ref state, s_visit);
            foreach (var match in state.Matches)
            {
                var payload = match.Projection.ProjectPayloadAsync(
                        subject,
                        context,
                        MessagePack.MessagePackSerializerOptions.Standard,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                sent += ProjectedDispatchBenchmarks.Dispatch(match.SubscriptionIds, s_eventType, payload);
            }
        }

        BenchmarkSink.Consume(sent);
    }

    private void AddSubscriptions(string[] fields)
    {
        var filter = ProjectedDispatchBenchmarks.Equal(nameof(UiSelectionChangedEvent.ElementId), "inventory");
        for (int projection = 0; projection < fields.Length; projection++)
        {
            var compiled = ProjectedDispatchBenchmarks.Compile<UiSelectionChangedEvent>(fields[projection]);
            for (int id = 0; id < SubscriptionsPerProjection; id++)
            {
                var kernel = FilterCompiler.Compile(typeof(UiSelectionChangedEvent), filter);
                _index.Add(new ProjectedDispatchSubscription<UiSelectionChangedEvent>(
                    _manualIds[projection][id],
                    kernel,
                    kernel.CreateMatcher<UiSelectionChangedEvent>(),
                    compiled), filter);
            }
        }
    }

    private static bool VisitCandidate(
        ProjectedDispatchSubscription<UiSelectionChangedEvent> subscription,
        ref ProjectedDispatchState<UiSelectionChangedEvent> state)
    {
        if (subscription.Matcher.Matches(state.Subject))
            state.Matches.Add(subscription.Id, subscription.Projection.Key, subscription.Projection);
        return true;
    }

    private static ProjectedEvent ClientProjection(UiSelectionChangedEvent item, int projection) =>
        ProjectedDispatchBenchmarks.OneField(
            s_eventType,
            nameof(UiSelectionChangedEvent),
            projection switch
            {
                0 => ProjectedValues.Field(nameof(UiSelectionChangedEvent.ElementId), ProjectedValues.String(item.ElementId)),
                1 => ProjectedValues.Field(nameof(UiSelectionChangedEvent.SelectedValue), ProjectedValues.String(item.SelectedValue)),
                _ => ProjectedValues.Field(nameof(UiSelectionChangedEvent.CharacterId), ProjectedValues.Integer(item.CharacterId)),
            });
}

internal static class ProjectedDispatchBenchmarks
{
    public static SubscriptionIdBatch[] CreateIdGroups(string prefix, int groupCount, int idsPerGroup)
    {
        var groups = new SubscriptionIdBatch[groupCount];
        for (int group = 0; group < groups.Length; group++)
        {
            var ids = new string[idsPerGroup];
            for (int id = 0; id < idsPerGroup; id++)
                ids[id] = $"{prefix}-{group}-{id}";
            groups[group] = new SubscriptionIdBatch(
                ids.Length,
                ids.Length > 0 ? ids[0] : null,
                ids.Length > 1 ? ids[1] : null,
                ids.Length > 2 ? ids[2] : null,
                ids.Length > 3 ? ids[3] : null,
                ids.Length > 4 ? ids[4..] : null);
        }

        return groups;
    }

    public static CompiledProjection<object> Compile<TEvent>(string field) =>
        ProjectionCompiler.Compile<object>(
            typeof(TEvent),
            EventProjectionExpression.Select(field),
            RejectInclude);

    public static FilterExpression Equal(string field, object value) =>
        FilterExpression.Compare(
            field,
            FilterOperator.Equal,
            FilterValue.FromObject(value));

    public static ProjectedEvent OneField(string eventType, string eventName, ProjectedEventField field) =>
        new() { EventType = eventType, EventName = eventName, Fields = [field] };

    public static long Dispatch(SubscriptionIdBatch subscriptionIds, string eventType, ProjectedEvent projected)
    {
        byte[] payload = MessagePack.MessagePackSerializer.Serialize(projected, MessagePack.MessagePackSerializerOptions.Standard);
        return Dispatch(subscriptionIds, eventType, payload);
    }

    public static long Dispatch(SubscriptionIdBatch subscriptionIds, string eventType, ReadOnlyMemory<byte> payload)
    {
        return subscriptionIds.Count + eventType.Length + payload.Length;
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

internal sealed record ProjectedDispatchSubscription<TSubject>(
    string Id,
    CompiledKernel Kernel,
    CompiledKernelMatcher<TSubject> Matcher,
    CompiledProjection<object> Projection);

internal struct ProjectedDispatchState<TSubject>
{
    public ProjectedDispatchState(TSubject subject)
    {
        Subject = subject;
        Matches = new ProjectionMatchAccumulator<CompiledProjection<object>>();
    }

    public TSubject Subject { get; }
    public ProjectionMatchAccumulator<CompiledProjection<object>> Matches;
}

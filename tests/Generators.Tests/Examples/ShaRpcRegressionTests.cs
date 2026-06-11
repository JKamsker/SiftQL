using SiftQL.Examples.ShaRpc.Client.Hosting;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ShaRpcRegressionTests
{
    [Fact]
    public async Task QueryAsyncReadsRowsStoredUnderAssignableRecordType()
    {
        var dataStore = new ServerDataStore();
        dataStore.Replace<IServerRecord>(
        [
            new ServerOfferSnapshot("north-gate", "offer-a", "potion", 25, 3, Enabled: true),
        ]);
        var server = new RemoteServerService(dataStore, new ClientMessageSink());
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(ServerOfferSnapshot.OfferCode)));

        IReadOnlyList<ProjectedEvent> results = await server.QueryAsync(
            new ServerQueryRequest(nameof(ServerOfferSnapshot), pipeline),
            CancellationToken.None);

        ProjectedEvent projected = Assert.Single(results);
        Assert.Equal("offer-a", projected.Field(nameof(ServerOfferSnapshot.OfferCode)).String);
    }

    [Fact]
    public async Task PublishAsyncDispatchesConcreteRecordThroughInterfaceTypedCall()
    {
        var client = new RecordingClient();
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        server.Attach(client);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(
                nameof(InventoryChangedEvent.Quantity),
                FilterOperator.Equal,
                FilterValue.From(3L)))
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.ItemCode)));
        await server.SubscribeAsync(
            new SubscriptionRequest("inventory-feed", nameof(InventoryChangedEvent), pipeline),
            CancellationToken.None);
        IServerRecord record = new InventoryChangedEvent(1001, "north-gate", "potion", 3, 10);

        await server.PublishAsync(record, CancellationToken.None);

        SubscriptionDispatch dispatch = Assert.Single(client.Dispatches);
        Assert.Equal("inventory-feed", dispatch.SubscriptionId);
        Assert.Equal("potion", dispatch.Payload.Field(nameof(InventoryChangedEvent.ItemCode)).String);
    }

    [Fact]
    public async Task PublishAsyncWithoutMatchingDispatchDoesNotRequireAttachedClient()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(
                nameof(InventoryChangedEvent.Quantity),
                FilterOperator.Equal,
                FilterValue.From(99L)))
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.ItemCode)));
        await server.SubscribeAsync(
            new SubscriptionRequest("inventory-feed", nameof(InventoryChangedEvent), pipeline),
            CancellationToken.None);

        await server.PublishAsync(
            new InventoryChangedEvent(1001, "north-gate", "potion", 3, 10),
            CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsyncRunsContextProjectionFilterPipeline()
    {
        var client = new RecordingClient();
        var context = new ServerLookupContext(
            new ClientProfile(1001, "Ari", ClientTier.Premium),
            new ClientProfile(1002, "Bryn", ClientTier.Standard));
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink(), context);
        server.Attach(client);
        var subscription = ServerKernel.ForInventoryChanged()
            .WithServerContext()
            .Where(static ev => ev.Region == "north-gate" && ev.Quantity > 0)
            .Select(static (ev, ctx) => new
            {
                ev.SessionId,
                ev.ItemCode,
                ev.Quantity,
                ClientTier = ctx.GetClient(ev.SessionId).Tier,
            })
            .Where(static ev => ev.ClientTier == ClientTier.Premium)
            .Select(static ev => new
            {
                Session = ev.SessionId,
                Item = ev.ItemCode,
                ev.Quantity,
                Tier = ev.ClientTier,
            });
        await server.SubscribeAsync(
            new SubscriptionRequest("premium-inventory-feed", nameof(InventoryChangedEvent), subscription.Pipeline),
            CancellationToken.None);

        await server.PublishAsync(
            new InventoryChangedEvent(1001, "north-gate", "potion", 2, 10),
            CancellationToken.None);
        await server.PublishAsync(
            new InventoryChangedEvent(1002, "north-gate", "ore", 2, 10),
            CancellationToken.None);

        SubscriptionDispatch dispatch = Assert.Single(client.Dispatches);
        Assert.Equal("premium-inventory-feed", dispatch.SubscriptionId);
        Assert.Equal(1001, dispatch.Payload.Field("Session").Integer);
        Assert.Equal("potion", dispatch.Payload.Field("Item").String);
        Assert.Equal(nameof(ClientTier.Premium), dispatch.Payload.Field("Tier").String);
    }

    [Fact]
    public async Task PublishAsyncSnapshotsSubscriptionsDuringAsyncDispatch()
    {
        var client = new BlockingClient();
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        server.Attach(client);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.ItemCode)));
        await server.SubscribeAsync(
            new SubscriptionRequest("first", nameof(InventoryChangedEvent), pipeline),
            CancellationToken.None);
        Task publish = server.PublishAsync(
            new InventoryChangedEvent(1001, "north-gate", "potion", 3, 10),
            CancellationToken.None);

        await client.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.SubscribeAsync(
            new SubscriptionRequest("second", nameof(InventoryChangedEvent), pipeline),
            CancellationToken.None);
        client.ReleaseDispatch();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));

        SubscriptionDispatch dispatch = Assert.Single(client.Dispatches);
        Assert.Equal("first", dispatch.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsyncRejectsPayloadWithoutIntegerSession()
    {
        var server = new RecordingServer();
        var client = new RemoteClientService();
        client.Attach(server);
        var dispatch = new SubscriptionDispatch(
            "inventory-feed",
            nameof(InventoryChangedEvent),
            new ProjectedEvent
            {
                EventType = "InventoryChangedEvent",
                EventName = "InventoryChangedEvent",
                Fields = [new ProjectedEventField("Item", ProjectedEventValue.FromScalar("potion"))],
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DispatchAsync(dispatch, CancellationToken.None));
    }

    [Fact]
    public async Task SendToClientAsyncRecordsConcurrentDeliveries()
    {
        var sink = new ClientMessageSink();
        var server = new RemoteServerService(new ServerDataStore(), sink);
        using var start = new ManualResetEventSlim(initialState: false);
        const int workers = 32;
        const int deliveriesPerWorker = 256;

        Task[] tasks = Enumerable.Range(0, workers)
            .Select(worker => Task.Run(async () =>
            {
                start.Wait();
                for (int i = 0; i < deliveriesPerWorker; i++)
                {
                    await server.SendToClientAsync(
                        new ClientDelivery(
                            worker,
                            "test",
                            new ProjectedEvent
                            {
                                EventType = "Test",
                                EventName = "Test",
                            }),
                        CancellationToken.None);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(workers * deliveriesPerWorker, sink.Deliveries.Count);
    }

    private sealed class RecordingClient : IRemoteClient
    {
        public List<SubscriptionDispatch> Dispatches { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            SubscriptionDispatch dispatch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dispatches.Add(dispatch);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingClient : IRemoteClient
    {
        private readonly TaskCompletionSource _releaseDispatch = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DispatchStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<SubscriptionDispatch> Dispatches { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task DispatchAsync(
            SubscriptionDispatch dispatch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dispatches.Add(dispatch);
            DispatchStarted.TrySetResult();
            await _releaseDispatch.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseDispatch() =>
            _releaseDispatch.TrySetResult();
    }

    private sealed class RecordingServer : IRemoteServer
    {
        public Task<ServerHello> HelloAsync(
            ClientHello hello,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServerHello("test", hello.KnownSubjects, OnlineClientCount: 1));

        public Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
            ServerQueryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectedEvent>>([]);

        public Task SubscribeAsync(
            SubscriptionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendToClientAsync(
            ClientDelivery delivery,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}

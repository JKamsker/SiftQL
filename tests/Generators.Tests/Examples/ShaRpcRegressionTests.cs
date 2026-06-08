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

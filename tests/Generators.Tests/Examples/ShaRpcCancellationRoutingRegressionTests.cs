using SiftQL.Examples.ShaRpc.Client.Hosting;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ShaRpcCancellationRoutingRegressionTests
{
    [Fact]
    public async Task QueryAsyncHonorsPreCanceledTokenWithoutRows()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            server.QueryAsync(
                new ServerQueryRequest(
                    nameof(ServerOfferSnapshot),
                    EventPipelineExpression.Default.AppendProjection(
                        EventProjectionExpression.Select(nameof(ServerOfferSnapshot.OfferCode)))),
                canceled.Token));
    }

    [Fact]
    public async Task PublishAsyncHonorsPreCanceledTokenWithoutSubscriptions()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            server.PublishAsync(
                new InventoryChangedEvent(1001, "north-gate", "potion", 3, 10),
                canceled.Token));
    }

    [Fact]
    public async Task DispatchAsyncRoutesBySubscriptionId()
    {
        var sink = new ClientMessageSink();
        var server = new RemoteServerService(new ServerDataStore(), sink);
        var client = new RemoteClientService();
        client.Attach(server);

        await client.DispatchAsync(Dispatch("inventory-feed"), CancellationToken.None);
        await client.DispatchAsync(Dispatch("premium-inventory-feed"), CancellationToken.None);

        Assert.Equal(
            ["inventory.notice", "inventory.premium"],
            sink.Deliveries.Select(static delivery => delivery.Channel).ToArray());
    }

    private static SubscriptionDispatch Dispatch(string subscriptionId) =>
        new(
            subscriptionId,
            nameof(InventoryChangedEvent),
            new ProjectedEvent
            {
                EventType = nameof(InventoryChangedEvent),
                EventName = nameof(InventoryChangedEvent),
                Fields =
                [
                    new ProjectedEventField("Session", ProjectedEventValue.FromScalar(1001L)),
                ],
            });
}

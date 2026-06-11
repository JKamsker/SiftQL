using SiftQL.Examples.ShaRpc.Client.Hosting;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Generators.Tests;

public sealed class ShaRpcOfferRetryRegressionTests
{
    [Fact]
    public async Task ClientStartAsyncCanRetryAfterAppliedOfferDeliveryWithoutDuplicates()
    {
        var sink = new ClientMessageSink();
        var server = new ThrowAfterFirstCatalogDeliveryServer(sink);
        var client = new RemoteClientService();
        client.Attach(server);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartAsync(CancellationToken.None));

        await client.StartAsync(CancellationToken.None);

        ClientDelivery[] catalog = sink.Deliveries
            .Where(static delivery => delivery.Channel == "catalog.offer")
            .ToArray();
        Assert.Equal(2, catalog.Length);
        Assert.Equal(["offer-a", "offer-b"], catalog.Select(static delivery => delivery.Payload.Field("Offer").String));
    }

    private sealed class ThrowAfterFirstCatalogDeliveryServer(ClientMessageSink sink) : IRemoteServer
    {
        private bool _thrown;

        public Task<ServerHello> HelloAsync(
            ClientHello hello,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ServerHello("retry-test", hello.KnownSubjects, OnlineClientCount: 1));
        }

        public Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
            ServerQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ProjectedEvent> offers =
            [
                Offer("offer-a", "potion", 25, 3),
                Offer("offer-b", "elixir", 50, 2),
            ];
            return Task.FromResult(offers);
        }

        public Task SubscribeAsync(
            SubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task SendToClientAsync(
            ClientDelivery delivery,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink.Add(delivery);
            if (!_thrown && delivery.Channel == "catalog.offer")
            {
                _thrown = true;
                throw new InvalidOperationException("delivery failed after apply");
            }

            return Task.CompletedTask;
        }

        private static ProjectedEvent Offer(string offer, string item, long cost, long stock) =>
            new()
            {
                EventType = nameof(ServerOfferSnapshot),
                EventName = nameof(ServerOfferSnapshot),
                Fields =
                [
                    new ProjectedEventField("Offer", ProjectedEventValue.FromScalar(offer)),
                    new ProjectedEventField("Item", ProjectedEventValue.FromScalar(item)),
                    new ProjectedEventField("Cost", ProjectedEventValue.FromScalar(cost)),
                    new ProjectedEventField("Stock", ProjectedEventValue.FromScalar(stock)),
                ],
            };
    }
}

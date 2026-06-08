using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Projected;

namespace SiftQL.Examples.ShaRpc.Client.Hosting;

public sealed class RemoteClientService : IRemoteClient
{
    private IRemoteServer? _server;

    public void Attach(IRemoteServer server) =>
        _server = server ?? throw new ArgumentNullException(nameof(server));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        IRemoteServer server = Server;
        ServerHello hello = await server.HelloAsync(
            new ClientHello(
                "catalog-client",
                ServerKernel.SubjectTypes.Select(static type => type.Name).ToArray()),
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"Connected to {hello.ServerName}; server subjects: {string.Join(", ", hello.Subjects)}");

        await QueryOffersAsync(server, cancellationToken).ConfigureAwait(false);
        await SubscribeInventoryAsync(server, cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchAsync(
        SubscriptionDispatch dispatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (!dispatch.Payload.TryGetField("Session", out ProjectedEventValue session) ||
            session.Kind != ProjectedEventValueKind.Integer)
        {
            throw new InvalidOperationException(
                "Subscription dispatch payload must include an integer Session field.");
        }

        long clientId = session.Integer;
        await Server.SendToClientAsync(
            new ClientDelivery(clientId, "inventory.notice", dispatch.Payload),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task QueryOffersAsync(
        IRemoteServer server,
        CancellationToken cancellationToken)
    {
        var query = ServerKernel.ForServerOffer()
            .InRegion("north-gate")
            .Where(static offer => offer.Enabled)
            .Select(static (offer, _) => new
            {
                Offer = offer.OfferCode,
                Item = offer.ItemCode,
                Cost = offer.Cost,
                Stock = offer.Stock,
            })
            .WhereProjected(static offer =>
                offer.Field("Stock").Integer > 0 &&
                offer.Field("Cost").Integer <= 50);

        IReadOnlyList<SiftQL.Projected.ProjectedEvent> offers = await server.QueryAsync(
            new ServerQueryRequest(nameof(ServerOfferSnapshot), query.Pipeline),
            cancellationToken).ConfigureAwait(false);

        foreach (var offer in offers)
        {
            await server.SendToClientAsync(
                new ClientDelivery(1001, "catalog.offer", offer),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task SubscribeInventoryAsync(
        IRemoteServer server,
        CancellationToken cancellationToken)
    {
        var subscription = ServerKernel.ForInventoryChanged()
            .InRegion("north-gate")
            .Where(static ev => ev.Quantity > 0)
            .Select(static (ev, _) => new
            {
                Session = ev.SessionId,
                Item = ev.ItemCode,
                Quantity = ev.Quantity,
            })
            .WhereProjected(static ev => ev.Field("Quantity").Integer >= 2);

        return server.SubscribeAsync(
            new SubscriptionRequest(
                "inventory-feed",
                nameof(InventoryChangedEvent),
                subscription.Pipeline),
            cancellationToken);
    }

    private IRemoteServer Server =>
        _server ?? throw new InvalidOperationException("No remote server proxy is attached.");
}

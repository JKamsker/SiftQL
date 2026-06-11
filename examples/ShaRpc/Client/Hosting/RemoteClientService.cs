using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Projected;

namespace SiftQL.Examples.ShaRpc.Client.Hosting;

public sealed class RemoteClientService : IRemoteClient
{
    private const int OffersDeliveredStep = 1;
    private const int InventorySubscribedStep = 2;
    private const int PremiumInventorySubscribedStep = 3;

    private readonly string _premiumInventoryRegion = "north-gate";
    private readonly SemaphoreSlim _startupGate = new(1, 1);
    private IRemoteServer? _server;
    private int _completedStartupStep;

    public void Attach(IRemoteServer server) =>
        _server = server ?? throw new ArgumentNullException(nameof(server));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completedStartupStep >= PremiumInventorySubscribedStep)
                return;

            IRemoteServer server = Server;
            ServerHello hello = await server.HelloAsync(
                new ClientHello(
                    "catalog-client",
                    ServerKernel.SubjectTypes.Select(static type => type.Name).ToArray()),
                cancellationToken).ConfigureAwait(false);

            Console.WriteLine(
                $"Connected to {hello.ServerName}; server subjects: {string.Join(", ", hello.Subjects)}");

            if (_completedStartupStep < OffersDeliveredStep)
            {
                await QueryOffersAsync(server, cancellationToken).ConfigureAwait(false);
                _completedStartupStep = OffersDeliveredStep;
            }

            if (_completedStartupStep < InventorySubscribedStep)
            {
                await SubscribeInventoryAsync(server, cancellationToken).ConfigureAwait(false);
                _completedStartupStep = InventorySubscribedStep;
            }

            if (_completedStartupStep < PremiumInventorySubscribedStep)
            {
                await SubscribePremiumInventoryAsync(server, cancellationToken).ConfigureAwait(false);
                _completedStartupStep = PremiumInventorySubscribedStep;
            }
        }
        finally
        {
            _startupGate.Release();
        }
    }

    public async Task DispatchAsync(
        SubscriptionDispatch dispatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        cancellationToken.ThrowIfCancellationRequested();
        if (!dispatch.Payload.TryGetField("Session", out ProjectedEventValue session) ||
            session.Kind != ProjectedEventValueKind.Integer)
        {
            throw new InvalidOperationException(
                "Subscription dispatch payload must include an integer Session field.");
        }

        long clientId = session.Integer;
        await Server.SendToClientAsync(
            new ClientDelivery(clientId, ChannelFor(dispatch.SubscriptionId), dispatch.Payload),
            cancellationToken).ConfigureAwait(false);
    }

    private static string ChannelFor(string subscriptionId) =>
        subscriptionId switch
        {
            "inventory-feed" => "inventory.notice",
            "premium-inventory-feed" => "inventory.premium",
            _ => throw new InvalidOperationException(
                $"Unknown subscription dispatch '{subscriptionId}'."),
        };

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
            string deliveryId = $"catalog.offer:1001:{offer.Field("Offer").String}";
            await server.SendToClientAsync(
                new ClientDelivery(1001, "catalog.offer", offer, deliveryId),
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

    private Task SubscribePremiumInventoryAsync(
        IRemoteServer server,
        CancellationToken cancellationToken)
    {
        var subscription = ServerKernel.ForInventoryChanged()
            .WithServerContext()
            .Where(ev => ev.Region == _premiumInventoryRegion && ev.Quantity > 0)
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

        return server.SubscribeAsync(
            new SubscriptionRequest(
                "premium-inventory-feed",
                nameof(InventoryChangedEvent),
                subscription.Pipeline),
            cancellationToken);
    }

    private IRemoteServer Server =>
        _server ?? throw new InvalidOperationException("No remote server proxy is attached.");
}

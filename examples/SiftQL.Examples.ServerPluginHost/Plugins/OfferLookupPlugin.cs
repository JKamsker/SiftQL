using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;

namespace SiftQL.Examples.ServerPluginHost.Plugins;

public sealed class OfferLookupPlugin : IServerPlugin
{
    public string Id => "offer-lookup";

    public void Configure(PluginRegistration registration)
    {
        registration.OnStart(static async (context, cancellationToken) =>
        {
            var offers = await context.Server.GetAsync(
                ServerKernel.ForServerOffer()
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
                        offer.Field("Cost").Integer <= 50),
                cancellationToken).ConfigureAwait(false);

            foreach (var offer in offers)
                context.Clients.SendToAvatar(1001, "server.offer", offer);
        });
    }
}

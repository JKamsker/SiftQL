using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Plugins;

public sealed class InventoryAuditPlugin : IServerPlugin
{
    public string Id => "inventory-audit";

    public void Configure(PluginRegistration registration)
    {
        registration.OnProjected(
            ServerKernel.ForItemUsed()
                .Consumable()
                .InRegion("north-gate")
                .Where(static ev => ev.Quantity > 0 && ev.RemainingCharges > 0)
                .Select(static (ev, _) => new
                {
                    RecipientAvatar = ev.AvatarId,
                    Region = ev.Region,
                    Item = ev.ItemCode,
                    Amount = ev.Quantity,
                })
                .WhereProjected(static ev => ev.Field("Amount").Integer >= 2),
            static (projected, context) =>
            {
                context.Clients.SendToAvatar(
                    AvatarId(projected),
                    "inventory.notice",
                    projected);
                return ValueTask.CompletedTask;
            });
    }

    private static long AvatarId(ProjectedEvent projected) =>
        projected.Field("RecipientAvatar").Integer;
}

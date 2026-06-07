using SiftQL;

namespace SiftQL.Examples.ServerPluginHost.Domain;

[KernelCatalog]
[KernelSubject(typeof(ItemUsedEvent), Alias = "ItemUsed")]
[KernelSubject(typeof(AbilityCastEvent), Alias = "AbilityCast")]
[KernelSubject(typeof(ZoneEnteredEvent), Alias = "ZoneEntered")]
[KernelSubject(typeof(ServerOfferSnapshot), Alias = "ServerOffer")]
public static partial class ServerKernel
{
    public static QueryKernel<AbilityCastEvent> CriticalHighPowerCast() =>
        ForAbilityCast()
            .Where(static ev => ev.Critical && ev.Power >= 100);
}

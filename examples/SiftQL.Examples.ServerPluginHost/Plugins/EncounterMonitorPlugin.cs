using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Plugins;

public sealed class EncounterMonitorPlugin : IServerPlugin
{
    public string Id => "encounter-monitor";

    public void Configure(PluginRegistration registration)
    {
        registration.OnProjected(
            ServerKernel.CriticalHighPowerCast()
                .InRegion("north-gate")
                .Select(static (ev, _) => new
                {
                    RecipientAvatar = ev.AvatarId,
                    Region = ev.Region,
                    Ability = ev.AbilityCode,
                    ThreatScore = ev.Power,
                })
                .WhereProjected(static ev => ev.Field("ThreatScore").Integer >= 120),
            static (projected, context) =>
            {
                context.Clients.SendToAvatar(
                    AvatarId(projected),
                    "encounter.notice",
                    projected);
                return ValueTask.CompletedTask;
            });
    }

    private static long AvatarId(ProjectedEvent projected) =>
        projected.Field("RecipientAvatar").Integer;
}

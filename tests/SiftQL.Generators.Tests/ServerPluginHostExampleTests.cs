using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;

using ExampleAbilityCastEvent = SiftQL.Examples.ServerPluginHost.Domain.AbilityCastEvent;
using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;
using ExampleZoneEnteredEvent = SiftQL.Examples.ServerPluginHost.Domain.ZoneEnteredEvent;

namespace SiftQL.Generators.Tests;

internal static class ServerPluginHostExampleTests
{
    public static void RunAll()
    {
        GeneratedKernelCatalogExposesEventModels();
        ProjectedClientDeliveryRunsFullPipeline();
    }

    private static void GeneratedKernelCatalogExposesEventModels()
    {
        AssertEx.True(
            ServerKernel.IsKnownSubject(typeof(ExampleItemUsedEvent)),
            "generated kernel catalog knows item events");
        AssertEx.True(
            ServerKernel.IsKnownSubject(typeof(ExampleAbilityCastEvent)),
            "generated kernel catalog knows ability events");
        AssertEx.True(
            ServerKernel.SubjectTypes.Contains(typeof(ExampleZoneEnteredEvent)),
            "generated kernel catalog exposes zone events");

        try
        {
            _ = ServerKernel.For<UnregisteredEvent>();
            throw new InvalidOperationException("unregistered kernel subject was accepted");
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ProjectedClientDeliveryRunsFullPipeline()
    {
        var clients = new ClientGateway();
        clients.Register(1001);
        clients.Register(1002);

        var host = new InMemoryServerPluginHost(clients);
        host.Register(new InventoryAuditPlugin());
        host.Register(new EncounterMonitorPlugin());

        PublishEvents(host);

        ClientSession avatar1001 = Session(clients, 1001);
        ClientSession avatar1002 = Session(clients, 1002);
        AssertEx.Equal(2, avatar1001.Messages.Count, "only matching projected events reached client 1001");
        AssertEx.Equal(0, avatar1002.Messages.Count, "client 1002 received no filtered events");

        ClientMessage inventory = avatar1001.Messages[0];
        AssertEx.Equal("inventory.notice", inventory.Channel, "inventory channel");
        AssertPayload(inventory.Payload, "EventName", "ItemUsedEvent");
        AssertPayload(inventory.Payload, "RecipientAvatar", 1001L);
        AssertPayload(inventory.Payload, "Amount", 2L);

        ClientMessage encounter = avatar1001.Messages[1];
        AssertEx.Equal("encounter.notice", encounter.Channel, "encounter channel");
        AssertPayload(encounter.Payload, "EventName", "AbilityCastEvent");
        AssertPayload(encounter.Payload, "Ability", "ember-lance");
        AssertPayload(encounter.Payload, "ThreatScore", 140L);
    }

    private static void PublishEvents(InMemoryServerPluginHost host)
    {
        host.PublishAsync(new ExampleItemUsedEvent(1001, "north-gate", "potion", "consumable", 2, 18))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        host.PublishAsync(new ExampleItemUsedEvent(1001, "north-gate", "ether", "consumable", 1, 8))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        host.PublishAsync(new ExampleItemUsedEvent(1002, "north-gate", "ore", "material", 3, 0))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        host.PublishAsync(new ExampleAbilityCastEvent(1001, "north-gate", "ember-lance", 140, true))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        host.PublishAsync(new ExampleAbilityCastEvent(1001, "north-gate", "spark", 110, true))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        host.PublishAsync(new ExampleAbilityCastEvent(1002, "harbor", "flare", 160, true))
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private static ClientSession Session(ClientGateway clients, long avatarId) =>
        clients.Sessions.Single(session => session.AvatarId == avatarId);

    private static void AssertPayload<TValue>(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        TValue expected)
    {
        AssertEx.True(payload.TryGetValue(key, out object? actual), $"payload contains {key}");
        AssertEx.Equal(expected, actual, $"payload value {key}");
    }

    private sealed record UnregisteredEvent : IFilterSubject;
}

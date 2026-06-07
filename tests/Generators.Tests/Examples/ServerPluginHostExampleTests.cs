using System.Buffers;
using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Serialization;
using SiftQL.Expressions;

using ExampleAbilityCastEvent = SiftQL.Examples.ServerPluginHost.Domain.AbilityCastEvent;
using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;
using ExampleServerOfferSnapshot = SiftQL.Examples.ServerPluginHost.Domain.ServerOfferSnapshot;
using ExampleZoneEnteredEvent = SiftQL.Examples.ServerPluginHost.Domain.ZoneEnteredEvent;
using ShaRpcServerKernel = SiftQL.Examples.ShaRpc.SharedContracts.Domain.ServerKernel;
using ShaRpcServerOfferSnapshot = SiftQL.Examples.ShaRpc.SharedContracts.Domain.ServerOfferSnapshot;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostExampleTests
{
    [Fact]
    public void GeneratedKernelCatalogExposesEventModels()
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
        AssertEx.True(
            ServerKernel.IsKnownSubject(typeof(ExampleServerOfferSnapshot)),
            "generated kernel catalog knows server offers");

        try
        {
            _ = ServerKernel.For<UnregisteredEvent>();
            throw new InvalidOperationException("unregistered kernel subject was accepted");
        }
        catch (ArgumentException)
        {
        }
    }

    [Fact]
    public async Task ProjectedClientDeliveryRunsFullPipeline()
    {
        var clients = new ClientGateway();
        clients.Register(1001);
        clients.Register(1002);

        ServerDataStore data = CreateServerData();
        var host = new InMemoryServerPluginHost(clients, data);
        host.Register(new OfferLookupPlugin());
        host.Register(new InventoryAuditPlugin());
        host.Register(new EncounterMonitorPlugin());

        await host.StartAsync();
        await PublishEvents(host);

        ClientSession avatar1001 = Session(clients, 1001);
        ClientSession avatar1002 = Session(clients, 1002);
        AssertEx.Equal(3, avatar1001.Messages.Count, "only matching projected events reached client 1001");
        AssertEx.Equal(0, avatar1002.Messages.Count, "client 1002 received no filtered events");

        ClientMessage offer = Message(avatar1001, "server.offer");
        AssertPayload(offer.Payload, "EventName", "ServerOfferSnapshot");
        AssertPayload(offer.Payload, "Offer", "field-kit");
        AssertPayload(offer.Payload, "Cost", 45L);
        AssertEx.True(!offer.Payload.ContainsKey("Enabled"), "server offer payload was projected");

        ClientMessage inventory = Message(avatar1001, "inventory.notice");
        AssertEx.Equal("inventory.notice", inventory.Channel, "inventory channel");
        AssertPayload(inventory.Payload, "EventName", "ItemUsedEvent");
        AssertPayload(inventory.Payload, "RecipientAvatar", 1001L);
        AssertPayload(inventory.Payload, "Amount", 2L);

        ClientMessage encounter = Message(avatar1001, "encounter.notice");
        AssertEx.Equal("encounter.notice", encounter.Channel, "encounter channel");
        AssertPayload(encounter.Payload, "EventName", "AbilityCastEvent");
        AssertPayload(encounter.Payload, "Ability", "ember-lance");
        AssertPayload(encounter.Payload, "ThreatScore", 140L);
    }

    [Fact]
    public void SharedShaRpcContractsExposeHostOwnedKernel()
    {
        AssertEx.True(
            ShaRpcServerKernel.IsKnownSubject(typeof(ShaRpcServerOfferSnapshot)),
            "shared ShaRPC kernel knows server offers");
        AssertEx.True(
            ShaRpcServerKernel.SubjectTypes.Contains(typeof(ShaRpcServerOfferSnapshot)),
            "shared ShaRPC kernel exposes server offers");
    }

    [Fact]
    public void SharedShaRpcQueryRoundTripsThroughSerializer()
    {
        var query = ShaRpcServerKernel.ForServerOffer()
            .Where(static offer => offer.Enabled)
            .Select(static (offer, _) => new
            {
                Offer = offer.OfferCode,
                Cost = offer.Cost,
            })
            .WhereProjected(static offer => offer.Field("Cost").Integer <= 50);
        var request = new ServerQueryRequest(nameof(ShaRpcServerOfferSnapshot), query.Pipeline);
        var writer = new ArrayBufferWriter<byte>();
        JsonRpcSerializerFactory.Create().Serialize(writer, request);

        ServerQueryRequest roundTripped = JsonRpcSerializerFactory
            .Create()
            .Deserialize<ServerQueryRequest>(writer.WrittenMemory);

        AssertEx.Equal(nameof(ShaRpcServerOfferSnapshot), roundTripped.Subject, "round-tripped subject");
        AssertEx.Equal(3, roundTripped.Pipeline.Stages.Length, "selection/projection/selection stages");
        AssertEx.Equal(EventPipelineStageKind.Filter, roundTripped.Pipeline.Stages[0].Kind, "source filter stage");
        AssertEx.Equal(EventPipelineStageKind.Projection, roundTripped.Pipeline.Stages[1].Kind, "projection stage");
        AssertEx.Equal(EventPipelineStageKind.Filter, roundTripped.Pipeline.Stages[2].Kind, "projected filter stage");
    }

    private static ServerDataStore CreateServerData()
    {
        var data = new ServerDataStore();
        data.Replace<ExampleServerOfferSnapshot>(
        [
            new("north-gate", "field-kit", "potion", 45, 3, true),
            new("north-gate", "vault-kit", "crystal", 75, 2, true),
            new("north-gate", "closed-cache", "token", 15, 6, false),
            new("harbor", "dock-kit", "potion", 35, 4, true),
        ]);
        return data;
    }

    private static async Task PublishEvents(InMemoryServerPluginHost host)
    {
        await host.PublishAsync(new ExampleItemUsedEvent(1001, "north-gate", "potion", "consumable", 2, 18));
        await host.PublishAsync(new ExampleItemUsedEvent(1001, "north-gate", "ether", "consumable", 1, 8));
        await host.PublishAsync(new ExampleItemUsedEvent(1002, "north-gate", "ore", "material", 3, 0));
        await host.PublishAsync(new ExampleAbilityCastEvent(1001, "north-gate", "ember-lance", 140, true));
        await host.PublishAsync(new ExampleAbilityCastEvent(1001, "north-gate", "spark", 110, true));
        await host.PublishAsync(new ExampleAbilityCastEvent(1002, "harbor", "flare", 160, true));
    }

    private static ClientSession Session(ClientGateway clients, long avatarId) =>
        clients.Sessions.Single(session => session.AvatarId == avatarId);

    private static ClientMessage Message(ClientSession session, string channel) =>
        session.Messages.Single(message => message.Channel == channel);

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

using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Projected;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostPayloadRegressionTests
{
    [Fact]
    public void ClientPayloadPreservesDecimalProjectedValues()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "decimal",
            new ProjectedEvent
            {
                EventType = "Test",
                EventName = "Test",
                Fields =
                [
                    new ProjectedEventField("Amount", ProjectedEventValue.FromScalar(1.25m)),
                ],
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        Assert.True(message.Payload.TryGetValue("Amount", out object? amount));
        Assert.Equal(1.25m, amount);
    }

    [Fact]
    public void ClientPayloadKeepsEventMetadataAndRejectsFieldOverwrite()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "metadata",
            new ProjectedEvent
            {
                EventType = "Game.Events.ItemUsedEvent",
                EventName = "ItemUsedEvent",
                Fields =
                [
                    new ProjectedEventField(
                        nameof(ProjectedEvent.EventType),
                        ProjectedEventValue.FromScalar("field-type")),
                    new ProjectedEventField(
                        nameof(ProjectedEvent.EventName),
                        ProjectedEventValue.FromScalar("field-name")),
                ],
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        Assert.Equal("Game.Events.ItemUsedEvent", message.Payload[nameof(ProjectedEvent.EventType)]);
        Assert.Equal("ItemUsedEvent", message.Payload[nameof(ProjectedEvent.EventName)]);
    }

    [Fact]
    public void ClientPayloadKeepsProjectedFieldWhenContextNameCollides()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "collision",
            new ProjectedEvent
            {
                EventType = "Test",
                EventName = "Test",
                Fields = [new ProjectedEventField("Item", ProjectedEventValue.FromScalar("field-item"))],
                Context = [new ProjectedEventField("Item", ProjectedEventValue.FromScalar("context-item"))],
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        Assert.Equal("field-item", message.Payload["Item"]);
    }

    [Fact]
    public void ClientPayloadTreatsNullFieldCollectionsAsEmpty()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "null-fields",
            new ProjectedEvent
            {
                EventType = "Test",
                EventName = "Test",
                Fields = null!,
                Context = null!,
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        Assert.Equal("Test", message.Payload[nameof(ProjectedEvent.EventType)]);
        Assert.Equal("Test", message.Payload[nameof(ProjectedEvent.EventName)]);
        Assert.Equal(2, message.Payload.Count);
    }

    [Fact]
    public void ClientPayloadSkipsNullFieldEntries()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "null-entries",
            new ProjectedEvent
            {
                EventType = "Test",
                EventName = "Test",
                Fields = [null!],
                Context = [null!],
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        Assert.Equal("Test", message.Payload[nameof(ProjectedEvent.EventType)]);
        Assert.Equal("Test", message.Payload[nameof(ProjectedEvent.EventName)]);
        Assert.Equal(2, message.Payload.Count);
    }

    [Fact]
    public void ClientPayloadSkipsNullNestedObjectEntries()
    {
        var clients = new ClientGateway();
        clients.Register(1001);

        bool sent = clients.SendToAvatar(
            1001,
            "nested-null-entries",
            new ProjectedEvent
            {
                EventType = "Test",
                EventName = "Test",
                Fields =
                [
                    new ProjectedEventField(
                        "Nested",
                        ProjectedEventValue.FromFields(
                        [
                            null!,
                            new ProjectedEventField("Name", ProjectedEventValue.FromScalar("ok")),
                        ])),
                ],
            });

        Assert.True(sent);
        ClientMessage message = Assert.Single(clients.Sessions.Single().Messages);
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(message.Payload["Nested"]);
        Assert.Equal("ok", nested["Name"]);
        Assert.Single(nested);
    }
}

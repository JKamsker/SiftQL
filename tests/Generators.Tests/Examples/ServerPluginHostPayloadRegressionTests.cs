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
}

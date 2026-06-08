using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ShaRpcSubscriptionRegistrationRegressionTests
{
    [Fact]
    public async Task SubscribeAsyncRejectsDuplicateSubscriptionIds()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.ItemCode)));
        var request = new SubscriptionRequest(
            "inventory-feed",
            nameof(InventoryChangedEvent),
            pipeline);

        await server.SubscribeAsync(request, CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SubscribeAsync(request, CancellationToken.None));
        Assert.Contains("inventory-feed", exception.Message, StringComparison.Ordinal);
    }
}

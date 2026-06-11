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
    public async Task SubscribeAsyncRejectsConflictingDuplicateSubscriptionIds()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        EventPipelineExpression firstPipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.ItemCode)));
        EventPipelineExpression secondPipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(InventoryChangedEvent.Quantity)));
        var request = new SubscriptionRequest(
            "inventory-feed",
            nameof(InventoryChangedEvent),
            firstPipeline);
        var duplicate = new SubscriptionRequest(
            "inventory-feed",
            nameof(InventoryChangedEvent),
            secondPipeline);

        await server.SubscribeAsync(request, CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SubscribeAsync(duplicate, CancellationToken.None));
        Assert.Contains("inventory-feed", exception.Message, StringComparison.Ordinal);
    }
}

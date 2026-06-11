using SiftQL;
using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Hosting;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostStartupFailureRegressionTests
{
    [Fact]
    public async Task StartAsyncFailureDoesNotReplayCompletedStartupHandlersOrReopenRegistration()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        int completedCalls = 0;
        host.RegisterStartup(
            "completed",
            (_, _) =>
            {
                completedCalls++;
                return ValueTask.CompletedTask;
            });
        host.RegisterStartup(
            "failing",
            static (_, _) => throw new InvalidOperationException("startup failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().AsTask());

        Assert.Equal(1, completedCalls);
        Assert.Throws<InvalidOperationException>(() => host.Register(new NoOpPlugin()));
    }

    [Fact]
    public async Task PublishAfterStartupFailureThrowsWithoutDispatchingSubscriptions()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        int dispatched = 0;
        host.SubscribeProjected(
            "plugin",
            QueryKernel.For<ItemUsedEvent>().Select(nameof(ItemUsedEvent.ItemId)),
            (_, _) =>
            {
                dispatched++;
                return ValueTask.CompletedTask;
            });
        host.RegisterStartup(
            "failing",
            static (_, _) => throw new InvalidOperationException("startup failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.PublishAsync(new ItemUsedEvent(Guid.NewGuid(), 7, 100, 1)).AsTask());
        Assert.Equal(0, dispatched);
    }

    [Fact]
    public async Task QueryAfterStartupFailureThrows()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        host.RegisterStartup(
            "failing",
            static (_, _) => throw new InvalidOperationException("startup failed"));
        QueryKernel<ItemUsedEvent> query = QueryKernel
            .For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.QueryProjectedAsync("plugin", query).AsTask());
    }

    private sealed class NoOpPlugin : IServerPlugin
    {
        public string Id => "late";

        public void Configure(PluginRegistration registration) => _ = registration;
    }
}

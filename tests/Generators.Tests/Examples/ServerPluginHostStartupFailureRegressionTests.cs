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

    private sealed class NoOpPlugin : IServerPlugin
    {
        public string Id => "late";

        public void Configure(PluginRegistration registration) => _ = registration;
    }
}

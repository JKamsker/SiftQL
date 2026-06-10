using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostStartingRegistrationRegressionTests
{
    [Fact]
    public async Task RegisterWhileStartAsyncIsRunningIsRejected()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Register(new BlockingStartupPlugin(entered, release));
        Task startTask = host.StartAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                host.Register(new OfferLookupPlugin()));

            Assert.Contains("started", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            release.SetResult();
            await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockingStartupPlugin(
        TaskCompletionSource entered,
        TaskCompletionSource release) : IServerPlugin
    {
        public string Id => "blocking-startup";

        public void Configure(PluginRegistration registration)
        {
            registration.OnStart(async (_, _) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });
        }
    }
}

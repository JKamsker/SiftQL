using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;

using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostCancellationRegressionTests
{
    [Fact]
    public async Task StartAsyncCancellationDoesNotLatchStartupFailure()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        using var canceled = new CancellationTokenSource();
        host.RegisterStartup(
            "cancel",
            (_, token) =>
            {
                canceled.Cancel();
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StartAsync(canceled.Token).AsTask());

        await host.StartAsync();
    }

    [Fact]
    public async Task StartAsyncCancellationDoesNotReplayCompletedStartupHandlers()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        using var canceled = new CancellationTokenSource();
        int completedCalls = 0;
        bool cancelFirstAttempt = true;
        host.RegisterStartup(
            "completed",
            (_, _) =>
            {
                completedCalls++;
                return ValueTask.CompletedTask;
            });
        host.RegisterStartup(
            "cancel-once",
            (_, token) =>
            {
                if (cancelFirstAttempt)
                {
                    cancelFirstAttempt = false;
                    canceled.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StartAsync(canceled.Token).AsTask());

        await host.StartAsync();

        Assert.Equal(1, completedCalls);
    }

    [Fact]
    public async Task PublishAsyncHonorsPreCanceledTokenWithoutSubscriptions()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            host.PublishAsync(
                new ExampleItemUsedEvent(1001, "north-gate", "potion", "consumable", 2, 18),
                canceled.Token).AsTask());
    }

    [Fact]
    public async Task QueryProjectedAsyncHonorsPreCanceledTokenWithoutRows()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            host.QueryProjectedAsync(
                "region-query",
                QueryKernel.For<IRegionEvent>().Select(nameof(IRegionEvent.Region)),
                canceled.Token).AsTask());
    }
}

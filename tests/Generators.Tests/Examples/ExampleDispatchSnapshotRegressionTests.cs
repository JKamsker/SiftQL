using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;

using HostItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;
using RpcInventoryChangedEvent = SiftQL.Examples.ShaRpc.SharedContracts.Domain.InventoryChangedEvent;
using RpcServerDataStore = SiftQL.Examples.ShaRpc.Server.Hosting.ServerDataStore;

namespace SiftQL.Generators.Tests;

public sealed class ExampleDispatchSnapshotRegressionTests
{
    [Fact]
    public async Task ServerPluginHostPublishDoesNotDispatchSubscriptionsAddedMidPublish()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        var seen = new List<string>();
        bool addedSecond = false;
        QueryKernel<HostItemUsedEvent> query = QueryKernel.For<HostItemUsedEvent>()
            .Select(nameof(HostItemUsedEvent.ItemCode));

        host.SubscribeProjected<HostItemUsedEvent>(
            "first",
            query,
            (_, _) =>
            {
                seen.Add("first");
                if (!addedSecond)
                {
                    addedSecond = true;
                    host.SubscribeProjected<HostItemUsedEvent>(
                        "second",
                        query,
                        (_, _) =>
                        {
                            seen.Add("second");
                            return ValueTask.CompletedTask;
                        });
                }

                return ValueTask.CompletedTask;
            });

        await host.PublishAsync(new HostItemUsedEvent(1001, "north", "potion", "consumable", 2, 18));

        Assert.Equal(["first"], seen);
    }

    [Fact]
    public async Task ShaRpcPublishUsesAttachedClientSnapshotForWholeDispatch()
    {
        var first = new BlockingClient();
        var second = new RecordingClient();
        var server = new RemoteServerService(new RpcServerDataStore(), new ClientMessageSink());
        server.Attach(first);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(RpcInventoryChangedEvent.ItemCode)));
        await server.SubscribeAsync(new SubscriptionRequest("first", "InventoryChangedEvent", pipeline));
        await server.SubscribeAsync(new SubscriptionRequest("second", "InventoryChangedEvent", pipeline));

        Task publish = server.PublishAsync(new RpcInventoryChangedEvent(1001, "north", "potion", 3, 10));
        await first.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.Attach(second);
        first.ReleaseDispatch();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["first", "second"], first.Dispatches.Select(static item => item.SubscriptionId));
        Assert.Empty(second.Dispatches);
    }

    private sealed class RecordingClient : IRemoteClient
    {
        public List<SubscriptionDispatch> Dispatches { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DispatchAsync(
            SubscriptionDispatch dispatch,
            CancellationToken cancellationToken = default)
        {
            Dispatches.Add(dispatch);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingClient : IRemoteClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<SubscriptionDispatch> Dispatches { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task DispatchAsync(
            SubscriptionDispatch dispatch,
            CancellationToken cancellationToken = default)
        {
            Dispatches.Add(dispatch);
            DispatchStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseDispatch() => _release.TrySetResult();
    }
}

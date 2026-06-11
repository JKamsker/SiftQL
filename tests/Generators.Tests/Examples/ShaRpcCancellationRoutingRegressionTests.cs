using SiftQL.Examples.ShaRpc.Client.Hosting;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ShaRpcCancellationRoutingRegressionTests
{
    [Fact]
    public async Task QueryAsyncHonorsPreCanceledTokenWithoutRows()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            server.QueryAsync(
                new ServerQueryRequest(
                    nameof(ServerOfferSnapshot),
                    EventPipelineExpression.Default.AppendProjection(
                        EventProjectionExpression.Select(nameof(ServerOfferSnapshot.OfferCode)))),
                canceled.Token));
    }

    [Fact]
    public async Task PublishAsyncHonorsPreCanceledTokenWithoutSubscriptions()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            server.PublishAsync(
                new InventoryChangedEvent(1001, "north-gate", "potion", 3, 10),
                canceled.Token));
    }

    [Fact]
    public async Task DispatchAsyncRoutesBySubscriptionId()
    {
        var sink = new ClientMessageSink();
        var server = new RemoteServerService(new ServerDataStore(), sink);
        var client = new RemoteClientService();
        client.Attach(server);

        await client.DispatchAsync(Dispatch("inventory-feed"), CancellationToken.None);
        await client.DispatchAsync(Dispatch("premium-inventory-feed"), CancellationToken.None);

        Assert.Equal(
            ["inventory.notice", "inventory.premium"],
            sink.Deliveries.Select(static delivery => delivery.Channel).ToArray());
    }

    [Fact]
    public async Task ClientStartAsyncCanRetryAfterSubscriptionCancellation()
    {
        var server = new RemoteServerService(new ServerDataStore(), new ClientMessageSink());
        var client = new RemoteClientService();
        using var cancel = new CancellationTokenSource();
        client.Attach(new CancelAfterInventorySubscribeServer(server, cancel));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.StartAsync(cancel.Token));

        client.Attach(server);
        await client.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ClientStartAsyncConcurrentCallsShareStartupAttempt()
    {
        var server = new BlockingStartupServer();
        var client = new RemoteClientService();
        client.Attach(server);

        Task first = client.StartAsync(CancellationToken.None);
        await server.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = client.StartAsync(CancellationToken.None);

        server.ReleaseQueries();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, server.QueryCalls);
        Assert.Equal(
            ["inventory-feed", "premium-inventory-feed"],
            server.SubscriptionIds);
    }

    private static SubscriptionDispatch Dispatch(string subscriptionId) =>
        new(
            subscriptionId,
            nameof(InventoryChangedEvent),
            new ProjectedEvent
            {
                EventType = nameof(InventoryChangedEvent),
                EventName = nameof(InventoryChangedEvent),
                Fields =
                [
                    new ProjectedEventField("Session", ProjectedEventValue.FromScalar(1001L)),
                ],
            });

    private sealed class CancelAfterInventorySubscribeServer(
        IRemoteServer inner,
        CancellationTokenSource cancel) : IRemoteServer
    {
        private bool _canceled;

        public Task<ServerHello> HelloAsync(
            ClientHello hello,
            CancellationToken cancellationToken = default) =>
            inner.HelloAsync(hello, cancellationToken);

        public Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
            ServerQueryRequest request,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(request, cancellationToken);

        public async Task SubscribeAsync(
            SubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            await inner.SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
            if (!_canceled &&
                string.Equals(request.SubscriptionId, "inventory-feed", StringComparison.Ordinal))
            {
                _canceled = true;
                cancel.Cancel();
            }
        }

        public Task SendToClientAsync(
            ClientDelivery delivery,
            CancellationToken cancellationToken = default) =>
            inner.SendToClientAsync(delivery, cancellationToken);
    }

    private sealed class BlockingStartupServer : IRemoteServer
    {
        private readonly TaskCompletionSource _releaseQueries = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private readonly List<string> _subscriptionIds = [];
        private readonly HashSet<string> _subscriptionIdsSeen = new(StringComparer.Ordinal);
        private int _queryCalls;

        public TaskCompletionSource QueryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int QueryCalls => Volatile.Read(ref _queryCalls);

        public string[] SubscriptionIds
        {
            get
            {
                lock (_gate)
                    return _subscriptionIds.ToArray();
            }
        }

        public Task<ServerHello> HelloAsync(
            ClientHello hello,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ServerHello("test", hello.KnownSubjects, OnlineClientCount: 1));
        }

        public async Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
            ServerQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            Interlocked.Increment(ref _queryCalls);
            QueryStarted.TrySetResult();
            await _releaseQueries.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return [];
        }

        public Task SubscribeAsync(
            SubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_subscriptionIdsSeen.Add(request.SubscriptionId))
                    throw new InvalidOperationException(
                        $"Subscription id '{request.SubscriptionId}' is already registered.");

                _subscriptionIds.Add(request.SubscriptionId);
            }

            return Task.CompletedTask;
        }

        public Task SendToClientAsync(
            ClientDelivery delivery,
            CancellationToken cancellationToken = default)
        {
            _ = delivery;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void ReleaseQueries() =>
            _releaseQueries.TrySetResult();
    }
}

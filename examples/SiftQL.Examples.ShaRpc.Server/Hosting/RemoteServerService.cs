using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class RemoteServerService(
    ServerDataStore dataStore,
    ClientMessageSink clients) : IRemoteServer
{
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];
    private IRemoteClient? _client;

    public void Attach(IRemoteClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<ServerHello> HelloAsync(
        ClientHello hello,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hello);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ServerHello(
            "in-memory-shard",
            ServerKernel.SubjectTypes.Select(static type => type.Name).ToArray(),
            OnlineClientCount: 2));
    }

    public async Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
        ServerQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Type subjectType = ResolveSubject(request.Subject);
        CompiledEventPipeline<object?> pipeline = Compile(subjectType, request.Pipeline);
        var results = new List<ProjectedEvent>();

        foreach (object row in dataStore.Rows(subjectType))
        {
            ProjectedEvent? projected = await pipeline
                .ProjectAsync(row, context: null, cancellationToken)
                .ConfigureAwait(false);
            if (projected is not null)
                results.Add(projected);
        }

        return results;
    }

    public Task SubscribeAsync(
        SubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Type subjectType = ResolveSubject(request.Subject);
        var subscription = new Subscription(
            request.SubscriptionId,
            subjectType.Name,
            Compile(subjectType, request.Pipeline));

        if (!_subscriptions.TryGetValue(subjectType, out List<Subscription>? subscriptions))
        {
            subscriptions = [];
            _subscriptions.Add(subjectType, subscriptions);
        }

        subscriptions.Add(subscription);
        return Task.CompletedTask;
    }

    public Task SendToClientAsync(
        ClientDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        clients.Add(delivery);
        return Task.CompletedTask;
    }

    public async Task PublishAsync<TRecord>(
        TRecord record,
        CancellationToken cancellationToken = default)
        where TRecord : IServerRecord
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_client is null)
            throw new InvalidOperationException("No remote client is attached.");
        if (!_subscriptions.TryGetValue(typeof(TRecord), out List<Subscription>? subscriptions))
            return;

        foreach (Subscription subscription in subscriptions)
        {
            ProjectedEvent? projected = await subscription.Pipeline
                .ProjectAsync(record, context: null, cancellationToken)
                .ConfigureAwait(false);
            if (projected is null)
                continue;

            await _client.DispatchAsync(
                new SubscriptionDispatch(subscription.Id, subscription.Subject, projected),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Type ResolveSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        foreach (Type type in ServerKernel.SubjectTypes)
        {
            if (string.Equals(subject, type.Name, StringComparison.Ordinal) ||
                string.Equals(subject, type.FullName, StringComparison.Ordinal))
            {
                return type;
            }
        }

        throw new InvalidOperationException($"Unknown subject '{subject}'.");
    }

    private static CompiledEventPipeline<object?> Compile(
        Type subjectType,
        EventPipelineExpression pipeline) =>
        EventPipelineCompiler.Compile<object?>(
            subjectType,
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate,
            message => new InvalidOperationException(message));

    private static CompiledProjection<object?>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Projection include '{include.Intrinsic}' is not supported here.");
    }

    private sealed record Subscription(
        string Id,
        string Subject,
        CompiledEventPipeline<object?> Pipeline);
}

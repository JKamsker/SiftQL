using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class RemoteServerService(
    ServerDataStore dataStore,
    ClientMessageSink clients,
    ServerLookupContext? queryContext = null) : IRemoteServer
{
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];
    private readonly Dictionary<string, Subscription> _subscriptionsById = new(StringComparer.Ordinal);
    private readonly object _subscriptionGate = new();
    private readonly ServerLookupContext _queryContext = queryContext ?? new ServerLookupContext();
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
        cancellationToken.ThrowIfCancellationRequested();
        Type subjectType = ResolveSubject(request.Subject);
        CompiledEventPipeline<ServerLookupContext> pipeline = Compile(subjectType, request.Pipeline);
        var results = new List<ProjectedEvent>();

        foreach (object row in dataStore.Rows(subjectType))
        {
            ProjectedEvent? projected = await pipeline
                .ProjectAsync(row, _queryContext, cancellationToken)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        Type subjectType = ResolveSubject(request.Subject);
        CompiledEventPipeline<ServerLookupContext> pipeline = Compile(subjectType, request.Pipeline);
        string pipelineSignature = pipeline.Key;
        var subscription = new Subscription(
            request.SubscriptionId,
            subjectType.Name,
            pipelineSignature,
            pipeline);

        lock (_subscriptionGate)
        {
            if (_subscriptionsById.TryGetValue(request.SubscriptionId, out Subscription? existing))
            {
                if (existing.Matches(subjectType.Name, pipelineSignature))
                    return Task.CompletedTask;

                throw new InvalidOperationException(
                    $"Subscription id '{request.SubscriptionId}' is already registered.");
            }

            if (!_subscriptions.TryGetValue(subjectType, out List<Subscription>? subscriptions))
            {
                subscriptions = [];
                _subscriptions.Add(subjectType, subscriptions);
            }

            _subscriptionsById.Add(request.SubscriptionId, subscription);
            subscriptions.Add(subscription);
        }

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
        cancellationToken.ThrowIfCancellationRequested();
        IRemoteClient? client = null;

        foreach (Subscription subscription in SubscriptionsFor(record))
        {
            ProjectedEvent? projected = await subscription.Pipeline
                .ProjectAsync(record, _queryContext, cancellationToken)
                .ConfigureAwait(false);
            if (projected is null)
                continue;

            client ??= _client ??
                throw new InvalidOperationException("No remote client is attached.");
            await client.DispatchAsync(
                new SubscriptionDispatch(subscription.Id, subscription.Subject, projected),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private Subscription[] SubscriptionsFor(object record)
    {
        Type recordType = record.GetType();
        var matches = new List<Subscription>();
        lock (_subscriptionGate)
        {
            foreach ((Type subjectType, List<Subscription> subscriptions) in _subscriptions)
            {
                if (!subjectType.IsAssignableFrom(recordType))
                    continue;

                matches.AddRange(subscriptions);
            }
        }

        return matches.ToArray();
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

    private static CompiledEventPipeline<ServerLookupContext> Compile(
        Type subjectType,
        EventPipelineExpression pipeline) =>
        EventPipelineCompiler.Compile<ServerLookupContext>(
            subjectType,
            pipeline,
            EventPipelineCompilerOptions.Immediate,
            message => new InvalidOperationException(message));

    private sealed record Subscription(
        string Id,
        string Subject,
        string PipelineSignature,
        CompiledEventPipeline<ServerLookupContext> Pipeline)
    {
        public bool Matches(string subject, string pipelineSignature) =>
            string.Equals(Subject, subject, StringComparison.Ordinal) &&
            string.Equals(PipelineSignature, pipelineSignature, StringComparison.Ordinal);
    }
}

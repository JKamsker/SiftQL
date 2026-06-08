using SiftQL;
using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Examples.ServerPluginHost.Hosting;

public sealed class InMemoryServerPluginHost
{
    private readonly ClientGateway _clients;
    private readonly ServerDataStore _serverData;
    private readonly HashSet<string> _pluginIds = new(StringComparer.Ordinal);
    private readonly List<StartupHandler> _startupHandlers = [];
    private readonly Dictionary<Type, List<ISubscription>> _subscriptions = [];

    public InMemoryServerPluginHost(ClientGateway clients)
        : this(clients, new ServerDataStore())
    {
    }

    public InMemoryServerPluginHost(ClientGateway clients, ServerDataStore serverData)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _serverData = serverData ?? throw new ArgumentNullException(nameof(serverData));
    }

    public void Register(IServerPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        string pluginId = plugin.Id;
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("Plugin id is required.", nameof(plugin));
        if (!_pluginIds.Add(pluginId))
            throw new InvalidOperationException($"Plugin id '{pluginId}' is already registered.");

        try
        {
            plugin.Configure(new PluginRegistration(pluginId, this));
        }
        catch
        {
            RemovePluginRegistrations(pluginId);
            _pluginIds.Remove(pluginId);
            throw;
        }
    }

    public void RegisterStartup(
        string pluginId,
        Func<PluginContext, CancellationToken, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(handler);
        _startupHandlers.Add(new StartupHandler(pluginId, handler));
    }

    public void SubscribeProjected<TEvent>(
        string pluginId,
        QueryKernel<TEvent> kernel,
        Func<ProjectedEvent, PluginContext, ValueTask> handler)
        where TEvent : IFilterSubject
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(handler);

        var pipeline = EventPipelineCompiler.Compile<PluginContext>(
            typeof(TEvent),
            kernel.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var subscription = new ProjectedSubscription<TEvent>(
            CreateContext(pluginId),
            pipeline,
            handler);

        if (!_subscriptions.TryGetValue(typeof(TEvent), out List<ISubscription>? subscriptions))
        {
            subscriptions = [];
            _subscriptions.Add(typeof(TEvent), subscriptions);
        }

        subscriptions.Add(subscription);
    }

    public async ValueTask PublishAsync<TEvent>(
        TEvent ev,
        CancellationToken cancellationToken = default)
        where TEvent : IFilterSubject
    {
        ArgumentNullException.ThrowIfNull(ev);
        foreach (var pair in _subscriptions)
        {
            if (!pair.Key.IsInstanceOfType(ev))
                continue;

            List<ISubscription> subscriptions = pair.Value;
            for (int i = 0; i < subscriptions.Count; i++)
                await subscriptions[i].DispatchAsync(ev, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < _startupHandlers.Count; i++)
        {
            StartupHandler startup = _startupHandlers[i];
            await startup.Handler(CreateContext(startup.PluginId), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<ProjectedEvent>> QueryProjectedAsync<TModel>(
        string pluginId,
        QueryKernel<TModel> query,
        CancellationToken cancellationToken = default)
        where TModel : IFilterSubject
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(query);
        var pipeline = EventPipelineCompiler.Compile<PluginContext>(
            typeof(TModel),
            query.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var results = new List<ProjectedEvent>();
        PluginContext context = CreateContext(pluginId);

        foreach (TModel row in _serverData.Rows<TModel>())
        {
            ProjectedEvent? projected = await pipeline
                .ProjectAsync(row, context, cancellationToken)
                .ConfigureAwait(false);
            if (projected is not null)
                results.Add(projected);
        }

        return results;
    }

    private PluginContext CreateContext(string pluginId) =>
        new(pluginId, _clients, new ServerQueryGateway(pluginId, this));

    private void RemovePluginRegistrations(string pluginId)
    {
        _startupHandlers.RemoveAll(handler =>
            string.Equals(handler.PluginId, pluginId, StringComparison.Ordinal));

        foreach (var pair in _subscriptions.ToArray())
        {
            pair.Value.RemoveAll(subscription =>
                string.Equals(subscription.PluginId, pluginId, StringComparison.Ordinal));
            if (pair.Value.Count == 0)
                _subscriptions.Remove(pair.Key);
        }
    }

    private static CompiledProjection<PluginContext>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Projection include '{include.Intrinsic}' is not supported here.");
    }

    private interface ISubscription
    {
        string PluginId { get; }
        ValueTask DispatchAsync(object ev, CancellationToken cancellationToken);
    }

    private sealed record StartupHandler(
        string PluginId,
        Func<PluginContext, CancellationToken, ValueTask> Handler);

    private sealed class ProjectedSubscription<TEvent>(
        PluginContext context,
        CompiledEventPipeline<PluginContext> pipeline,
        Func<ProjectedEvent, PluginContext, ValueTask> handler) : ISubscription
        where TEvent : IFilterSubject
    {
        public string PluginId => context.PluginId;

        public async ValueTask DispatchAsync(object ev, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectedEvent? projected = await pipeline
                .ProjectAsync((TEvent)ev, context, cancellationToken)
                .ConfigureAwait(false);
            if (projected is not null)
                await handler(projected, context).ConfigureAwait(false);
        }
    }
}

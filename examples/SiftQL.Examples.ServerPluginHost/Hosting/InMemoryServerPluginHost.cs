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
    private readonly Dictionary<Type, List<ISubscription>> _subscriptions = [];

    public InMemoryServerPluginHost(ClientGateway clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public void Register(IServerPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        plugin.Configure(new PluginRegistration(plugin.Id, this));
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
            new PluginContext(pluginId, _clients),
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
        if (!_subscriptions.TryGetValue(typeof(TEvent), out List<ISubscription>? subscriptions))
            return;

        for (int i = 0; i < subscriptions.Count; i++)
            await subscriptions[i].DispatchAsync(ev, cancellationToken).ConfigureAwait(false);
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
        ValueTask DispatchAsync(object ev, CancellationToken cancellationToken);
    }

    private sealed class ProjectedSubscription<TEvent>(
        PluginContext context,
        CompiledEventPipeline<PluginContext> pipeline,
        Func<ProjectedEvent, PluginContext, ValueTask> handler) : ISubscription
        where TEvent : IFilterSubject
    {
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

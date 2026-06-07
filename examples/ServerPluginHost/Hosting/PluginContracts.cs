using SiftQL;
using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Hosting;

public interface IServerPlugin
{
    string Id { get; }
    void Configure(PluginRegistration registration);
}

public sealed class PluginRegistration(string pluginId, InMemoryServerPluginHost host)
{
    public void OnStart(Func<PluginContext, CancellationToken, ValueTask> handler) =>
        host.RegisterStartup(pluginId, handler);

    public void OnProjected<TEvent>(
        QueryKernel<TEvent> kernel,
        Func<ProjectedEvent, PluginContext, ValueTask> handler)
        where TEvent : IFilterSubject =>
        host.SubscribeProjected(pluginId, kernel, handler);
}

public sealed record PluginContext(
    string PluginId,
    ClientGateway Clients,
    ServerQueryGateway Server);

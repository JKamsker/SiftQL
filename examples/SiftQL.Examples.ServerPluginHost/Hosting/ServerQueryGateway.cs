using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Hosting;

public sealed class ServerQueryGateway(string pluginId, InMemoryServerPluginHost host)
{
    public ValueTask<IReadOnlyList<ProjectedEvent>> GetAsync<TModel>(
        QueryKernel<TModel> query,
        CancellationToken cancellationToken = default)
        where TModel : IFilterSubject =>
        host.QueryProjectedAsync(pluginId, query, cancellationToken);
}

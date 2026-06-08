using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostRegistrationRegressionTests
{
    [Fact]
    public void RegisterRejectsDuplicatePluginIds()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        host.Register(new InventoryAuditPlugin());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            host.Register(new InventoryAuditPlugin()));

        Assert.Contains("inventory-audit", exception.Message, StringComparison.Ordinal);
    }
}

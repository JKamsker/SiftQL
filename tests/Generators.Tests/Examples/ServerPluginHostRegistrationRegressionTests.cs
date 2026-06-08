using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;
using SiftQL.Projected;
using Xunit;
using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;

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

    [Fact]
    public async Task RegisterRollsBackHandlersWhenConfigureFails()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        var plugin = new ThrowingProjectedPlugin();

        Assert.Throws<InvalidOperationException>(() => host.Register(plugin));

        await host.PublishAsync(
            new ExampleItemUsedEvent(1001, "north-gate", "potion", "consumable", 2, 18));

        Assert.False(plugin.Invoked);
    }

    private sealed class ThrowingProjectedPlugin : IServerPlugin
    {
        public string Id => "throwing-projected";
        public bool Invoked { get; private set; }

        public void Configure(PluginRegistration registration)
        {
            registration.OnProjected(
                QueryKernel.For<ExampleItemUsedEvent>(),
                Handler);
            throw new InvalidOperationException("configure failed");
        }

        private ValueTask Handler(ProjectedEvent projected, PluginContext context)
        {
            _ = projected;
            _ = context;
            Invoked = true;
            return ValueTask.CompletedTask;
        }
    }
}

using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;
using SiftQL.Projected;
using Xunit;
using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;
using ExampleServerOfferSnapshot = SiftQL.Examples.ServerPluginHost.Domain.ServerOfferSnapshot;

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

    [Fact]
    public async Task StartAsyncDoesNotReplayStartupHandlers()
    {
        var clients = new ClientGateway();
        clients.Register(1001);
        var serverData = new ServerDataStore();
        serverData.Replace<ExampleServerOfferSnapshot>(
        [
            new("north-gate", "offer-a", "potion", 45, 3, Enabled: true),
        ]);
        var host = new InMemoryServerPluginHost(clients, serverData);
        host.Register(new OfferLookupPlugin());

        await host.StartAsync();
        await host.StartAsync();

        ClientSession session = Assert.Single(clients.Sessions);
        Assert.Single(session.Messages, static message => message.Channel == "server.offer");
    }

    [Fact]
    public async Task RegisterAfterStartRejectsPluginRegistration()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());

        await host.StartAsync();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            host.Register(new OfferLookupPlugin()));
        Assert.Contains("started", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowerLevelRegistrationsAfterStartAreRejected()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());

        await host.StartAsync();

        InvalidOperationException startup = Assert.Throws<InvalidOperationException>(() =>
            host.RegisterStartup("late-startup", static (_, _) => ValueTask.CompletedTask));
        InvalidOperationException projected = Assert.Throws<InvalidOperationException>(() =>
            host.SubscribeProjected<ExampleItemUsedEvent>(
                "late-projected",
                QueryKernel.For<ExampleItemUsedEvent>(),
                static (_, _) => ValueTask.CompletedTask));

        Assert.Contains("started", startup.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("started", projected.Message, StringComparison.OrdinalIgnoreCase);
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

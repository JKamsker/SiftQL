using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Projected;

using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostDispatchRegressionTests
{
    [Fact]
    public async Task InterfaceProjectedSubscriptionReceivesConcreteRegionEvents()
    {
        var host = new InMemoryServerPluginHost(new ClientGateway());
        List<ProjectedEvent> seen = [];

        host.SubscribeProjected<IRegionEvent>(
            "region-subscription",
            QueryKernel.For<IRegionEvent>()
                .Where(static ev => ev.Region == "north-gate")
                .Select(nameof(IRegionEvent.Region)),
            (ev, _) =>
            {
                seen.Add(ev);
                return ValueTask.CompletedTask;
            });

        await host.PublishAsync(
            new ExampleItemUsedEvent(1001, "north-gate", "potion", "consumable", 2, 18));

        ProjectedEvent projected = Assert.Single(seen);
        Assert.Equal("north-gate", projected.Field(nameof(IRegionEvent.Region)).String);
    }
}

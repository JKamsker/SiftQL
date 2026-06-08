using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Projected;

using ExampleItemUsedEvent = SiftQL.Examples.ServerPluginHost.Domain.ItemUsedEvent;

namespace SiftQL.Generators.Tests;

public sealed class ServerPluginHostQueryRegressionTests
{
    [Fact]
    public async Task InterfaceProjectedQueryReadsConcreteStoredRows()
    {
        var data = new ServerDataStore();
        data.Replace<ExampleItemUsedEvent>(
        [
            new(1001, "north-gate", "potion", "consumable", 2, 18),
            new(1002, "harbor", "ore", "material", 1, 0),
        ]);
        var host = new InMemoryServerPluginHost(new ClientGateway(), data);

        IReadOnlyList<ProjectedEvent> projected = await host.QueryProjectedAsync(
            "region-query",
            QueryKernel.For<IRegionEvent>()
                .Where(static ev => ev.Region == "north-gate")
                .Select(nameof(IRegionEvent.Region)));

        ProjectedEvent result = Assert.Single(projected);
        Assert.Equal("north-gate", result.Field(nameof(IRegionEvent.Region)).String);
    }
}

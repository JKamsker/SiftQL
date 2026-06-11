using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedContextFilterRegressionTests
{
    [Fact]
    public async Task ContextWhereReadsProjectedEventFieldCalls()
    {
        QueryKernel<ProjectedEvent> query = QueryKernel
            .For<ProjectedEvent, FlagContext>()
            .Where(static (ev, ctx) => ev.Field("ItemId").Integer == 100 && ctx.Enabled())
            .Select(ProjectedEventPaths.Field("ItemId"))
            .ToQueryKernel();
        CompiledEventPipeline<FlagContext> compiled = EventPipelineCompiler.Compile<FlagContext>(
            typeof(ProjectedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            ProjectedItem(100),
            new FlagContext(true),
            CancellationToken.None);
        ProjectedEvent? wrongItem = await compiled.ProjectAsync(
            ProjectedItem(101),
            new FlagContext(true),
            CancellationToken.None);
        ProjectedEvent? disabled = await compiled.ProjectAsync(
            ProjectedItem(100),
            new FlagContext(false),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(wrongItem);
        Assert.Null(disabled);
    }

    private static ProjectedEvent ProjectedItem(long itemId) =>
        new()
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields =
            [
                new ProjectedEventField("ItemId", ProjectedEventValue.FromScalar(itemId)),
            ],
        };

    private sealed record FlagContext(bool Value)
    {
        public bool Enabled() => Value;
    }
}

using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectedSelectorIncludeAliasRegressionTests
{
    [Fact]
    public async Task ProjectedSelectorWithCapturedValueReadsMostRecentProjectedAlias()
    {
        const string label = "client-label";
        QueryKernel<ItemUsedEvent> query = QueryKernel.For<ItemUsedEvent>()
            .Select(new EventProjectionField(nameof(ItemUsedEvent.Quantity), "Amount"))
            .WhereProjected(static ev => ev.Field("Amount").Integer >= 2)
            .Select(new EventProjectionField(nameof(ItemUsedEvent.Quantity), "Quantity"))
            .Select(static ev => new
            {
                ev.Quantity,
                Label = label,
            });

        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            query.Pipeline,
            ProjectionContextIncludeCompiler.Compile<object>,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 100, 3),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(3, projected!.Field(nameof(ItemUsedEvent.Quantity)).Integer);
        Assert.Equal(label, projected.Field("Label").String);
    }

}

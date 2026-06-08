using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectedSelectRegressionTests
{
    [Fact]
    public async Task ProjectedEventStringSelectReadsProjectedFields()
    {
        QueryKernel<ProjectedEvent> kernel = QueryKernel
            .For<ProjectedEvent>()
            .Select("ItemId");
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            kernel.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent projected = await compiled.ProjectAsync(
            new ProjectedEvent
            {
                EventType = "Projected",
                EventName = "Projected",
                Fields =
                [
                    new ProjectedEventField("ItemId", ProjectedEventValue.FromScalar(100L)),
                ],
            },
            new object(),
            CancellationToken.None) ?? throw new InvalidOperationException("Projection was filtered out.");

        Assert.True(projected.TryGetField("ItemId", out ProjectedEventValue itemId));
        Assert.Equal(100, itemId.Integer);
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

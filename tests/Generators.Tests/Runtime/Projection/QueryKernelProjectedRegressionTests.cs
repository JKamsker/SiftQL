using SiftQL;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectedRegressionTests
{
    [Fact]
    public async Task AliasedProjectionFieldCanFeedLaterProjectedSelector()
    {
        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Select(static (ev, _) => new { Amount = ev.Quantity })
            .WhereProjected(static ev => ev.Field("Amount").Integer >= 2)
            .Select(static (ev, _) => new { Amount = ev.Quantity })
            .Pipeline;

        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 100, 3),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(ProjectedEventValueKind.Integer, projected!.Field("Amount").Kind);
        Assert.Equal(3, projected.Field("Amount").Integer);
    }

    [Fact]
    public async Task ProjectedEventWhereProjectedFiltersExistingProjectedFields()
    {
        QueryKernel<ProjectedEvent> kernel = QueryKernel.For<ProjectedEvent>()
            .WhereProjected(static ev => ev.Field("ItemId").Integer == 100);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            kernel.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields =
            [
                new ProjectedEventField("ItemId", ProjectedEventValue.FromScalar(100L)),
            ],
        };

        ProjectedEvent? projected = await compiled.ProjectAsync(
            source,
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(100, projected!.Field("ItemId").Integer);
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

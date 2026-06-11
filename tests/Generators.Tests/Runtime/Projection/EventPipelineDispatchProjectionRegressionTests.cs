using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineDispatchProjectionRegressionTests
{
    [Fact]
    public async Task DispatchPipelinePreservesProjectedFilterBeforeExplicitProjection()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field("ItemId"),
                FilterOperator.Equal,
                FilterValue.From(100L)))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId"),
            ]));

        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            dispatch,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            ProjectedItem(100),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            ProjectedItem(101),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task DispatchPipelineDropsSourceOnlyConjunctsFromMixedPreProjectionFilter()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.And(
                FilterExpression.Compare("SourceId", FilterOperator.Equal, FilterValue.From(7L)),
                FilterExpression.Compare(
                    ProjectedEventPaths.Field("ItemId"),
                    FilterOperator.Equal,
                    FilterValue.From(100L))))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId"),
            ]));

        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            dispatch,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            ProjectedItem(100),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            ProjectedItem(101),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
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
}

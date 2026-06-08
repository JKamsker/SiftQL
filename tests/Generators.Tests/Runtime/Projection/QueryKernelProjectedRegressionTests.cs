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
    public async Task RepeatedProjectedSelectUsesAliasedStageInput()
    {
        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Select(new EventProjectionField(nameof(ItemUsedEvent.Quantity), "Amount"))
            .WhereProjected(static ev => ev.Field("Amount").Integer >= 2)
            .Select(new EventProjectionField(nameof(ItemUsedEvent.Quantity), "Quantity"))
            .Select(new EventProjectionField(nameof(ItemUsedEvent.Quantity), "QuantityAgain"))
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
        Assert.Equal(ProjectedEventValueKind.Integer, projected!.Field("QuantityAgain").Kind);
        Assert.Equal(3, projected.Field("QuantityAgain").Integer);
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

    [Fact]
    public async Task ProjectedFilterReadsFlatDottedProjectionField()
    {
        FilterSchema.RegisterValueObject(typeof(PlayerDetails));
        EventPipelineExpression pipeline = QueryKernel.For<PlayerNestedEvent>()
            .Select("Player.Id")
            .WhereProjected(static ev => ev.Field("Player.Id").Integer == 42)
            .Pipeline;
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(PlayerNestedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new PlayerNestedEvent(new PlayerDetails(42), 2),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(42, projected!.Field("Player.Id").Integer);
    }

    [Fact]
    public async Task ProjectedSelectorRebasesNestedObjectFieldThroughAlias()
    {
        FilterSchema.RegisterValueObject(typeof(PlayerDetails));
        EventPipelineExpression pipeline = QueryKernel.For<PlayerNestedEvent>()
            .Select(
                new EventProjectionField(nameof(PlayerNestedEvent.Player), "P"),
                new EventProjectionField(nameof(PlayerNestedEvent.Quantity)))
            .WhereProjected(static ev =>
                ev.Field(nameof(PlayerNestedEvent.Quantity)).Integer == 2)
            .Select(static (ev, _) => new { PlayerId = ev.Player.Id })
            .Pipeline;
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(PlayerNestedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new PlayerNestedEvent(new PlayerDetails(42), 2),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(ProjectedEventValueKind.Integer, projected!.Field("PlayerId").Kind);
        Assert.Equal(42, projected.Field("PlayerId").Integer);
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record PlayerNestedEvent(PlayerDetails Player, int Quantity) : IFilterSubject;

    private sealed record PlayerDetails(int Id);
}

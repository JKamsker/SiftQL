using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectionSelectorRegressionTests
{
    private readonly string _instanceName = "client-field";

    [Fact]
    public async Task SelectorProjectsCapturedLocalVariableAsField()
    {
        string name = "client-local";
        QueryKernel<ItemUsedEvent> query = QueryKernel.For<ItemUsedEvent>()
            .Select(ev => new
            {
                ev.ItemId,
                Name = name,
            });

        ProjectedEvent projected = await ProjectAsync(query);

        Assert.Equal(42, projected.Field(nameof(ItemUsedEvent.ItemId)).Integer);
        Assert.Equal(name, projected.Field("Name").String);
        Assert.False(projected.TryGetContext("Name", out _));
        Assert.Contains(
            FirstProjection(query).Includes,
            static include => EventProjectionConstantIntrinsics.IsConstant(include.Intrinsic));
    }

    [Fact]
    public async Task SelectorProjectsCapturedInstanceFieldAsField()
    {
        QueryKernel<ItemUsedEvent> query = QueryKernel.For<ItemUsedEvent>()
            .Select(ev => new
            {
                ev.Quantity,
                Name = _instanceName,
            });

        ProjectedEvent projected = await ProjectAsync(query);

        Assert.Equal(5, projected.Field(nameof(ItemUsedEvent.Quantity)).Integer);
        Assert.Equal(_instanceName, projected.Field("Name").String);
    }

    [Fact]
    public async Task ProjectionContextSelectorProjectsStaticMemberAsField()
    {
        QueryKernel<ItemUsedEvent> query = QueryKernel.For<ItemUsedEvent>()
            .Select(static (_, _) => new { Value = StaticProjectionValue });

        ProjectedEvent projected = await ProjectAsync(query);

        Assert.Equal(42, projected.Field("Value").Integer);
    }

    [Fact]
    public async Task ProjectedSelectorProjectsCapturedLocalVariableAsField()
    {
        string name = "typed-projected";
        QueryKernel<ItemUsedEvent> query = QueryKernel
            .For<ItemUsedEvent, object>()
            .Select(static (ev, _) => new { ev.ItemId })
            .Select(ev => new
            {
                ev.ItemId,
                Name = name,
            });

        ProjectedEvent projected = await ProjectAsync(query);

        Assert.Equal(42, projected.Field(nameof(ItemUsedEvent.ItemId)).Integer);
        Assert.Equal(name, projected.Field("Name").String);
    }

    [Fact]
    public async Task ProjectedSelectorProjectsStaticMemberAsField()
    {
        QueryKernel<ItemUsedEvent> query = QueryKernel
            .For<ItemUsedEvent, object>()
            .Select(static (ev, _) => new { ev.ItemId })
            .Select(static _ => new { Value = StaticProjectionValue });

        ProjectedEvent projected = await ProjectAsync(query);

        Assert.Equal(42, projected.Field("Value").Integer);
    }

    [Fact]
    public async Task SelectorWithOnlyCapturedValueDoesNotExpandDefaultSourceProjection()
    {
        string label = "client-only";
        QueryKernel<WideProjectionEvent> query = QueryKernel.For<WideProjectionEvent>()
            .Select(_ => new { Label = label });
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(WideProjectionEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new WideProjectionEvent(),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Single(projected!.Fields);
        Assert.Equal(label, projected.Field("Label").String);
    }

    private static int StaticProjectionValue => 42;

    private static EventProjectionExpression FirstProjection(QueryKernel<ItemUsedEvent> query) =>
        query.Pipeline.Stages
            .First(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection;

    private static async Task<ProjectedEvent> ProjectAsync(QueryKernel<ItemUsedEvent> query)
    {
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);
        return await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 42, 5),
            new object(),
            CancellationToken.None) ?? throw new InvalidOperationException("Projection was filtered out.");
    }

    private sealed class WideProjectionEvent : IFilterSubject
    {
        public int Field00 { get; init; }
        public int Field01 { get; init; }
        public int Field02 { get; init; }
        public int Field03 { get; init; }
        public int Field04 { get; init; }
        public int Field05 { get; init; }
        public int Field06 { get; init; }
        public int Field07 { get; init; }
        public int Field08 { get; init; }
        public int Field09 { get; init; }
        public int Field10 { get; init; }
        public int Field11 { get; init; }
        public int Field12 { get; init; }
        public int Field13 { get; init; }
        public int Field14 { get; init; }
        public int Field15 { get; init; }
        public int Field16 { get; init; }
        public int Field17 { get; init; }
        public int Field18 { get; init; }
        public int Field19 { get; init; }
        public int Field20 { get; init; }
        public int Field21 { get; init; }
        public int Field22 { get; init; }
        public int Field23 { get; init; }
        public int Field24 { get; init; }
        public int Field25 { get; init; }
        public int Field26 { get; init; }
        public int Field27 { get; init; }
        public int Field28 { get; init; }
        public int Field29 { get; init; }
        public int Field30 { get; init; }
        public int Field31 { get; init; }
        public int Field32 { get; init; }
        public int Field33 { get; init; }
        public int Field34 { get; init; }
        public int Field35 { get; init; }
        public int Field36 { get; init; }
        public int Field37 { get; init; }
        public int Field38 { get; init; }
        public int Field39 { get; init; }
        public int Field40 { get; init; }
        public int Field41 { get; init; }
        public int Field42 { get; init; }
        public int Field43 { get; init; }
        public int Field44 { get; init; }
        public int Field45 { get; init; }
        public int Field46 { get; init; }
        public int Field47 { get; init; }
        public int Field48 { get; init; }
        public int Field49 { get; init; }
        public int Field50 { get; init; }
        public int Field51 { get; init; }
        public int Field52 { get; init; }
        public int Field53 { get; init; }
        public int Field54 { get; init; }
        public int Field55 { get; init; }
        public int Field56 { get; init; }
        public int Field57 { get; init; }
        public int Field58 { get; init; }
        public int Field59 { get; init; }
        public int Field60 { get; init; }
        public int Field61 { get; init; }
        public int Field62 { get; init; }
        public int Field63 { get; init; }
        public int Field64 { get; init; }
    }
}

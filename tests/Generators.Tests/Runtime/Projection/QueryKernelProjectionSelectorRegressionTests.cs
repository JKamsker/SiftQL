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
}

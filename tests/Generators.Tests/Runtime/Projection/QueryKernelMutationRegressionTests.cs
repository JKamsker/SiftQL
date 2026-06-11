using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelMutationRegressionTests
{
    [Fact]
    public async Task WithSourceFilterSnapshotsCallerOwnedFilter()
    {
        FilterExpression raw = FilterExpression.In(
            nameof(ItemUsedEvent.ItemId),
            [FilterValue.From(100L)]);
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .WithSourceFilter(raw)
            .Select(nameof(ItemUsedEvent.ItemId));

        raw.Values[0] = FilterValue.From(200L);

        CompiledEventPipeline<object> compiled = Compile(kernel.Pipeline);

        Assert.NotNull(await Project(compiled, Event(itemId: 100)));
        Assert.Null(await Project(compiled, Event(itemId: 200)));
    }

    [Fact]
    public async Task IncludeSnapshotsCallerOwnedArguments()
    {
        var include = new EventProjectionInclude(
            "test.limit",
            "limit",
            [new EventProjectionArgument("limit", FilterValue.From(3L))]);
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Include(include);

        include.Arguments[0] = new EventProjectionArgument("limit", FilterValue.From(9L));

        CompiledEventPipeline<object> compiled = Compile(kernel.Pipeline);
        ProjectedEvent? projected = await Project(compiled, Event(itemId: 100));

        Assert.NotNull(projected);
        Assert.True(projected!.TryGetContext("limit", out ProjectedEventValue value));
        Assert.Equal(3, value.Integer);
    }

    [Fact]
    public void AssigningDefaultPipelineClearsCopiedKernelState()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Where(static ev => ev.ItemId == 100)
            .Select(nameof(ItemUsedEvent.ItemId));

        QueryKernel<ItemUsedEvent> cleared = kernel with
        {
            Pipeline = EventPipelineExpression.Default,
        };

        Assert.Equal(FilterExpressionKind.Any, cleared.Filter.Kind);
        Assert.True(cleared.Projection.IsDefault);
        Assert.Empty(cleared.Pipeline.Stages);
    }

    [Fact]
    public void ExplicitDefaultPipelinePreservesInitializerFilterAndProjection()
    {
        var kernel = new QueryKernel<ItemUsedEvent>
        {
            Filter = FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                FilterValue.From(100L)),
            Projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)),
            Pipeline = EventPipelineExpression.Default,
        };

        Assert.Equal(FilterExpressionKind.Compare, kernel.Filter.Kind);
        Assert.False(kernel.Projection.IsDefault);
        Assert.Equal(2, kernel.Pipeline.Stages.Length);
    }

    [Fact]
    public void AssigningDefaultPipelineClearsLegacyInitializedCopiedKernelState()
    {
        var kernel = new QueryKernel<ItemUsedEvent>
        {
            Filter = FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                FilterValue.From(100L)),
            Projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)),
        };

        QueryKernel<ItemUsedEvent> cleared = kernel with
        {
            Pipeline = EventPipelineExpression.Default,
        };

        Assert.Equal(FilterExpressionKind.Any, cleared.Filter.Kind);
        Assert.True(cleared.Projection.IsDefault);
        Assert.Empty(cleared.Pipeline.Stages);
    }

    private static CompiledEventPipeline<object> Compile(EventPipelineExpression pipeline) =>
        EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileLimitInclude,
            EventPipelineCompilerOptions.Immediate);

    private static Task<ProjectedEvent?> Project(
        CompiledEventPipeline<object> compiled,
        ItemUsedEvent subject) =>
        compiled.ProjectAsync(subject, new object(), CancellationToken.None).AsTask();

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), 7, itemId, 2);

    private static CompiledProjection<object>.IncludeProjector CompileLimitInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        int limit = ProjectionIncludeArguments.RequiredInt(include, "limit");
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(limit)));
    }
}

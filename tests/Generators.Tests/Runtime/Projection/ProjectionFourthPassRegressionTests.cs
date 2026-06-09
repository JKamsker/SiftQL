using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionFourthPassRegressionTests
{
    [Fact]
    public async Task ProjectedEventDispatchPipelinePreservesFieldsReferencedByIndexedFilter()
    {
        QueryKernel<ProjectedEvent> kernel = QueryKernel.For<ProjectedEvent>()
            .WhereProjected(static ev => ev.Field("ItemId").Integer == 100);
        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(kernel.Pipeline);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            dispatch,
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
        Assert.True(projected!.TryGetField("ItemId", out ProjectedEventValue item));
        Assert.Equal(100, item.Integer);
    }

    [Fact]
    public async Task ProjectionDispatchPipelinePreservesProjectedFilterWhenProjectionIsSynthesized()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default.AppendFilter(
            FilterExpression.Compare(
                ProjectedEventPaths.Field("ItemId"),
                FilterOperator.Equal,
                FilterValue.From(100L)));
        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            dispatch,
            RejectInclude,
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
    public async Task ProjectedSelectorRebasesAliasedSourcePathCaseInsensitively()
    {
        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Select(new EventProjectionField("quantity", "Amount"))
            .WhereProjected(static ev => ev.Field("Amount").Integer >= 2)
            .Select(nameof(ItemUsedEvent.Quantity))
            .Pipeline;
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 100, 2),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(2, projected!.Field(nameof(ItemUsedEvent.Quantity)).Integer);
    }

    [Fact]
    public void ProjectionKeysSeparateParameterizedIncludeArgumentValues()
    {
        CompiledProjection<object> three = CompileLimitProjection(
            FilterValue.From(3L) with { ParameterKey = "p0" });
        CompiledProjection<object> five = CompileLimitProjection(
            FilterValue.From(5L) with { ParameterKey = "p0" });
        var accumulator = new ProjectionMatchAccumulator<CompiledProjection<object>>();

        accumulator.Add("three", three.Key, three);
        accumulator.Add("five", five.Key, five);

        Assert.NotEqual(three.Key, five.Key);
        Assert.Equal(2, accumulator.GroupCount);
    }

    [Fact]
    public void PipelineInitKeepsFilterAndProjectionStateInSync()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        var kernel = new QueryKernel<ItemUsedEvent>
        {
            Pipeline = EventPipelineExpression.From(
                filter,
                EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId))),
        };

        QueryKernel<ItemUsedEvent> changed = kernel with
        {
            Projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)),
        };

        Assert.Contains(changed.Pipeline.Stages, stage =>
            stage.Kind == EventPipelineStageKind.Filter &&
            stage.Filter.Field == nameof(ItemUsedEvent.ItemId));
        Assert.Equal(
            nameof(ItemUsedEvent.Quantity),
            changed.Pipeline.Stages.Last(static stage => stage.Kind == EventPipelineStageKind.Projection)
                .Projection.Fields.Single().Path);
    }

    [Fact]
    public async Task ProjectedStringContainsMatchesSubstring()
    {
        QueryKernel<ProjectedEvent> kernel = QueryKernel.For<ProjectedEvent>()
            .WhereProjected(static ev => ev.Field("Name").String!.Contains("alp"));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            kernel.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? matched = await compiled.ProjectAsync(
            ProjectedName("alphabet"),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            ProjectedName("beta"),
            new object(),
            CancellationToken.None);

        Assert.NotNull(matched);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task ProjectedFilterTreatsNullFieldsArrayAsMissing()
    {
        CompiledEventPipeline<object> compiled = CompileProjectedExistsPipeline();

        ProjectedEvent? result = await compiled.ProjectAsync(
            new ProjectedEvent { Fields = null! },
            new object(),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ProjectedFilterTreatsNullFieldEntriesAsMissing()
    {
        CompiledEventPipeline<object> compiled = CompileProjectedExistsPipeline();

        ProjectedEvent? result = await compiled.ProjectAsync(
            new ProjectedEvent { Fields = [null!] },
            new object(),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void PipelineProjectionStageWithNullProjectionThrowsValidationException()
    {
        var pipeline = new EventPipelineExpression
        {
            Stages =
            [
                new EventPipelineStage
                {
                    Kind = EventPipelineStageKind.Projection,
                    Projection = null!,
                },
            ],
        };

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    [Fact]
    public void PipelineFilterStageWithNullFilterThrowsValidationException()
    {
        var pipeline = new EventPipelineExpression
        {
            Stages =
            [
                new EventPipelineStage
                {
                    Kind = EventPipelineStageKind.Filter,
                    Filter = null!,
                },
            ],
        };

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    private static CompiledEventPipeline<object> CompileProjectedExistsPipeline() =>
        EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventPipelineExpression.Default.AppendFilter(
                FilterExpression.Exists(ProjectedEventPaths.Field("ItemId"))),
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    private static ProjectedEvent ProjectedName(string name) =>
        new()
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields =
            [
                new ProjectedEventField("Name", ProjectedEventValue.FromScalar(name)),
            ],
        };

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

    private static CompiledProjection<object> CompileLimitProjection(FilterValue value) =>
        ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude(
                    "test.limit",
                    "limit",
                    [new EventProjectionArgument("count", value)]),
            ]),
            CompileLimitInclude,
            ProjectionCompilerOptions.Immediate);

    private static CompiledProjection<object>.IncludeProjector CompileLimitInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        int limit = ProjectionIncludeArguments.RequiredInt(include, "count");
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(limit)));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

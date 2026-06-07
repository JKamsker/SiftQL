using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectionRegressionTests
{
    [Fact]
    public void PipelineLazyInitReflectsCurrentFilterAndProjection()
    {
        var kernel = new QueryKernel<ItemUsedEvent>
        {
            Filter = FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                FilterValue.From(100L)),
            Projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)),
        };

        EventPipelineExpression pipeline = kernel.Pipeline;

        Assert.NotNull(pipeline);
        Assert.True(pipeline.Stages.Length > 0);
    }

    [Fact]
    public void SelectOnNonProjectedKernelDoesNotAddFieldPrefix()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity));

        EventProjectionExpression projection = kernel.Pipeline.Stages
            .Last(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection;

        foreach (EventProjectionField field in projection.Fields)
        {
            Assert.False(
                field.Path.StartsWith(ProjectedEventPaths.FieldPrefix, StringComparison.Ordinal),
                $"Field '{field.Path}' should not have '{ProjectedEventPaths.FieldPrefix}' prefix on non-projected kernel");
        }
    }

    [Fact]
    public void SelectorProjectionAfterProjectedFilterReadsProjectedFields()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(static ev => ev.ItemId, static ev => ev.Quantity)
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(static (ev, _) => new { ev.Quantity });

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Field(nameof(ItemUsedEvent.Quantity)), field.Path);
    }

    [Fact]
    public void ExplicitProjectedPathIsNotDoublePrefixed()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)));

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)), field.Path);
    }

    [Fact]
    public void ExplicitProjectedContextPathIsNotRebased()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(new EventProjectionField(ProjectedEventPaths.Context("tag"), "tag"));

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Context("tag"), field.Path);
    }

    [Fact]
    public void ProjectedDecimalMemberAccessIsAccepted()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.Quantity))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.Quantity)).Decimal > 0m);

        FilterExpression filter = kernel.Pipeline.Stages
            .Last(static stage => stage.Kind == EventPipelineStageKind.Filter)
            .Filter;

        Assert.Equal(ProjectedEventPaths.Field(nameof(ItemUsedEvent.Quantity)), filter.Field);
    }

    [Fact]
    public void ProjectedBooleanMemberAccessIsAccepted()
    {
        var boolKernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.Quantity))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.Quantity)).Boolean);

        Assert.NotNull(boolKernel.Pipeline);
    }

    [Fact]
    public void ChainedProjectedValueMemberAccessIsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<ItemUsedEvent>()
                .Select(nameof(ItemUsedEvent.ItemId))
                .WhereProjected(static projected =>
                    projected.Field(nameof(ItemUsedEvent.ItemId)).String!.Length > 0));
    }

    [Fact]
    public void UnsupportedProjectedValueMemberIsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<ItemUsedEvent>()
                .Select(nameof(ItemUsedEvent.ItemId))
                .WhereProjected(static projected =>
                    projected.Field(nameof(ItemUsedEvent.ItemId)).Kind ==
                    ProjectedEventValueKind.Integer));
    }

    [Fact]
    public void RawSourceFilterParametersDoNotCollideWithCapturedFilter()
    {
        int itemId = 100;
        var rawQuantity = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(2L) with { ParameterKey = "p0" });

        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Where(ev => ev.ItemId == itemId)
            .WithSourceFilter(rawQuantity)
            .Pipeline;

        Assert.Equal("p1", pipeline.Stages[1].Filter.Value?.ParameterKey);
        AssertFilter(
            EventPipelineCompiler.SourceFilter(pipeline),
            new FilterCase(new ItemUsedEvent(Guid.NewGuid(), 7, 100, 2), true),
            new FilterCase(new ItemUsedEvent(Guid.NewGuid(), 7, 100, 100), false),
            new FilterCase(new ItemUsedEvent(Guid.NewGuid(), 7, 2, 2), false));
    }

    [Fact]
    public async Task RawIncludeParametersDoNotCollideWithCapturedFilter()
    {
        int itemId = 100;
        var rawInclude = new EventProjectionInclude(
            "test.limit",
            "limit",
            [new EventProjectionArgument(
                "limit",
                FilterValue.From(3L) with { ParameterKey = "p0" })]);

        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Where(ev => ev.ItemId == itemId)
            .Include(rawInclude)
            .Pipeline;

        Assert.Equal("p1", pipeline.Stages[1].Projection.Includes[0].Arguments[0].Value.ParameterKey);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileLimitInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 100, 2),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.True(projected!.TryGetContext("limit", out var limit));
        Assert.Equal(3, limit.Integer);
    }

    [Fact]
    public void RawFiltersRejectConflictingDuplicateParameterKeys()
    {
        FilterValue itemId = FilterValue.From(100L) with { ParameterKey = "p0" };
        FilterValue quantity = FilterValue.From(2L) with { ParameterKey = "p0" };
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                itemId),
            FilterExpression.Compare(
                nameof(ItemUsedEvent.Quantity),
                FilterOperator.Equal,
                quantity));

        var exception = Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), filter));

        Assert.Contains("p0", exception.Message);
    }

    [Fact]
    public void RawProjectionIncludesRejectConflictingDuplicateParameterKeys()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.limit",
                "limit",
                [
                    new EventProjectionArgument(
                        "first",
                        FilterValue.From(1L) with { ParameterKey = "p0" }),
                    new EventProjectionArgument(
                        "second",
                        FilterValue.From(2L) with { ParameterKey = "p0" }),
                ]),
        ]);

        var exception = Assert.Throws<FilterValidationException>(() =>
            ProjectionCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                projection,
                CompileNoopInclude));

        Assert.Contains("p0", exception.Message);
    }

    private static EventProjectionExpression LastProjection(QueryKernel<ItemUsedEvent> kernel) =>
        kernel.Pipeline.Stages
            .Last(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection;

    private static void AssertFilter(FilterExpression filter, params FilterCase[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

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

    private static CompiledProjection<object>.IncludeProjector CompileNoopInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.Null));
    }

    private sealed record FilterCase(ItemUsedEvent Subject, bool Expected);
}

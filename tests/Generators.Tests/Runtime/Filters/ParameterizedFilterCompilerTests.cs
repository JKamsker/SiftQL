using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Parameterized;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Tiered;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ParameterizedFilterCompilerTests : IDisposable
{
    private const int MinimumQuantityTwo = 2;
    private const int MinimumQuantityThree = 3;

    public ParameterizedFilterCompilerTests() =>
        ParameterizedFilterPlanCache.ClearForTests();

    [Fact]
    public void PlanCacheReusesShapeAndBindsCurrentParameterValues()
    {
        FilterExpression firstFilter = ItemIdEquals(100);
        CompiledKernel first = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            firstFilter,
            FilterCompilerOptions.Immediate);

        FilterExpression secondFilter = ItemIdEquals(200);
        CompiledKernel second = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            secondFilter,
            FilterCompilerOptions.Immediate);

        Assert.True(first.Matches(Event(itemId: 100)));
        Assert.False(first.Matches(Event(itemId: 200)));
        Assert.True(second.Matches(Event(itemId: 200)));
        Assert.False(second.Matches(Event(itemId: 100)));

        ParameterizedFilterPlanCacheSnapshot snapshot = ParameterizedFilterPlanCache.Snapshot;
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(2, snapshot.Requests);
        Assert.Equal(1, snapshot.Hits);
        Assert.Equal(1, snapshot.Misses);
    }

    [Fact]
    public void PlanCacheSeparatesEquivalentExpressionsForDifferentSchemaInstances()
    {
        FilterExpression expression = FilterExpression.Compare(
            "Flag",
            FilterOperator.Equal,
            FilterValue.From(true) with { ParameterKey = "p0" });

        CompiledKernel falseSchema = CompileProjectedFlag(expression, static _ => false);
        CompiledKernel trueSchema = CompileProjectedFlag(expression, static _ => true);

        Assert.False(falseSchema.Matches(new ProjectedEvent()));
        Assert.True(trueSchema.Matches(new ProjectedEvent()));

        ParameterizedFilterPlanCacheSnapshot snapshot = ParameterizedFilterPlanCache.Snapshot;
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, snapshot.Misses);
    }

    [Fact]
    public async Task TieredParameterizedFilterPromotesWithoutChangingBoundValues()
    {
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            ItemIdEquals(300),
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
            });

        Assert.True(kernel.Matches(Event(itemId: 300)));

        await WaitForSnapshotAsync(kernel, static item => item.Tier == TieredKernelTier.Compiled);

        Assert.True(kernel.Matches(Event(itemId: 300)));
        Assert.False(kernel.Matches(Event(itemId: 301)));
    }

    [Fact]
    public void TieredParameterizedFilterRejectsConflictingDuplicateParameterKeys()
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
            FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered));

        Assert.Contains("p0", exception.Message);
    }

    [Fact]
    public async Task TieredFilterPromotionUsesCompileTimeExpressionSnapshot()
    {
        FilterExpression filter = FilterExpression.In(
            nameof(ItemUsedEvent.ItemId),
            [FilterValue.From(100L)]);
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 2,
            });

        Assert.True(kernel.Matches(Event(itemId: 100)));
        filter.Values[0] = FilterValue.From(200L);
        Assert.True(kernel.Matches(Event(itemId: 100)));

        await WaitForSnapshotAsync(kernel, static item => item.Tier == TieredKernelTier.Compiled);

        Assert.True(kernel.Matches(Event(itemId: 100)));
        Assert.False(kernel.Matches(Event(itemId: 200)));
    }

    [Fact]
    public async Task QueryKernelCapturedVariableAndPropertyRebindsAndKeepsSingleSourceFilter()
    {
        var criteria = new MutableCriteria { CharacterId = 7 };
        int minimumQuantity = 2;
        ItemUsedEvent[] events =
        [
            Event(characterId: 7, itemId: 100, quantity: 1),
            Event(characterId: 7, itemId: 101, quantity: 2),
            Event(characterId: 8, itemId: 102, quantity: 2),
            Event(characterId: 8, itemId: 103, quantity: 3),
            Event(characterId: 7, itemId: 104, quantity: 5),
        ];

        QueryKernel<ItemUsedEvent> firstQuery = BuildParameterizedQuery(criteria, minimumQuantity);
        CompiledEventPipeline<object> firstPipeline = CompilePipeline(firstQuery);
        AssertSingleSourceFilter(firstQuery, firstPipeline.IndexFilter);
        long[] firstItemIds = await ProjectItemIdsAsync(firstPipeline, events);

        criteria.CharacterId = 8;
        QueryKernel<ItemUsedEvent> secondQuery = BuildParameterizedQuery(criteria, minimumQuantity);
        CompiledEventPipeline<object> secondPipeline = CompilePipeline(secondQuery);
        AssertSingleSourceFilter(secondQuery, secondPipeline.IndexFilter);
        long[] secondItemIds = await ProjectItemIdsAsync(secondPipeline, events);

        Assert.Equal([101L, 104L], firstItemIds);
        Assert.Equal([102L, 103L], secondItemIds);
        Assert.NotEqual(firstItemIds, secondItemIds);

        ParameterizedFilterPlanCacheSnapshot snapshot = ParameterizedFilterPlanCache.Snapshot;
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(2, snapshot.Requests);
        Assert.Equal(1, snapshot.Hits);
        Assert.Equal(1, snapshot.Misses);
    }

    [Fact]
    public async Task QueryKernelDifferentCompileTimeConstantsReusePlanAndBindCurrentValues()
    {
        ItemUsedEvent[] events =
        [
            Event(characterId: 7, itemId: 200, quantity: 1),
            Event(characterId: 7, itemId: 201, quantity: 2),
            Event(characterId: 7, itemId: 202, quantity: 3),
        ];

        QueryKernel<ItemUsedEvent> firstQuery = BuildConstantMinimumQuantityTwoQuery();
        CompiledEventPipeline<object> firstPipeline = CompilePipeline(firstQuery);
        AssertSingleSourceFilter(firstQuery, firstPipeline.IndexFilter, expectedCompareNodes: 1);
        long[] firstItemIds = await ProjectItemIdsAsync(firstPipeline, events);

        QueryKernel<ItemUsedEvent> secondQuery = BuildConstantMinimumQuantityThreeQuery();
        CompiledEventPipeline<object> secondPipeline = CompilePipeline(secondQuery);
        AssertSingleSourceFilter(secondQuery, secondPipeline.IndexFilter, expectedCompareNodes: 1);
        long[] secondItemIds = await ProjectItemIdsAsync(secondPipeline, events);

        Assert.Equal([201L, 202L], firstItemIds);
        Assert.Equal([202L], secondItemIds);
        Assert.NotEqual(firstItemIds, secondItemIds);

        ParameterizedFilterPlanCacheSnapshot snapshot = ParameterizedFilterPlanCache.Snapshot;
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(2, snapshot.Requests);
        Assert.Equal(1, snapshot.Hits);
        Assert.Equal(1, snapshot.Misses);
    }

    public void Dispose() =>
        ParameterizedFilterPlanCache.ClearForTests();

    private static FilterExpression ItemIdEquals(int itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId) with { ParameterKey = "p0" });

    private static QueryKernel<ItemUsedEvent> BuildParameterizedQuery(
        MutableCriteria criteria,
        int minimumQuantity) =>
        QueryKernel
            .For<ItemUsedEvent>()
            .Select(static item => item.ItemId)
            .Where(item =>
                item.CharacterId == criteria.CharacterId &&
                item.Quantity >= minimumQuantity);

    private static QueryKernel<ItemUsedEvent> BuildConstantMinimumQuantityTwoQuery() =>
        QueryKernel
            .For<ItemUsedEvent>()
            .Select(static item => item.ItemId)
            .Where(static item => item.Quantity >= MinimumQuantityTwo);

    private static QueryKernel<ItemUsedEvent> BuildConstantMinimumQuantityThreeQuery() =>
        QueryKernel
            .For<ItemUsedEvent>()
            .Select(static item => item.ItemId)
            .Where(static item => item.Quantity >= MinimumQuantityThree);

    private static CompiledEventPipeline<object> CompilePipeline(QueryKernel<ItemUsedEvent> query) =>
        EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            query.Pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    private static async Task<long[]> ProjectItemIdsAsync(
        CompiledEventPipeline<object> pipeline,
        IReadOnlyList<ItemUsedEvent> events)
    {
        var itemIds = new List<long>();
        for (int i = 0; i < events.Count; i++)
        {
            ProjectedEvent? projected = await pipeline.ProjectAsync(
                events[i],
                new object(),
                CancellationToken.None);
            if (projected is not null)
                itemIds.Add(projected.Field(nameof(ItemUsedEvent.ItemId)).Integer);
        }

        return itemIds.ToArray();
    }

    private static void AssertSingleSourceFilter(
        QueryKernel<ItemUsedEvent> query,
        FilterExpression compiledIndexFilter,
        int expectedCompareNodes = 2)
    {
        Assert.Equal(2, query.Pipeline.Stages.Length);
        Assert.Equal(EventPipelineStageKind.Filter, query.Pipeline.Stages[0].Kind);
        Assert.Equal(EventPipelineStageKind.Projection, query.Pipeline.Stages[1].Kind);
        Assert.Single(query.Pipeline.Stages, static stage => stage.Kind == EventPipelineStageKind.Filter);
        Assert.Equal(expectedCompareNodes, CountCompareNodes(EventPipelineCompiler.SourceFilter(query.Pipeline)));
        Assert.Equal(expectedCompareNodes, CountCompareNodes(compiledIndexFilter));
        Assert.Equal(expectedCompareNodes, KernelParameterKeyRewriter.ParameterCount(query.Pipeline));
    }

    private static int CountCompareNodes(FilterExpression expression)
    {
        int count = expression.Kind == FilterExpressionKind.Compare ? 1 : 0;
        for (int i = 0; i < expression.Children.Length; i++)
            count += CountCompareNodes(expression.Children[i]);
        return count;
    }

    private static CompiledKernel CompileProjectedFlag(
        FilterExpression expression,
        Func<object, object?> getter) =>
        FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            expression,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => new FilterSchema(
                typeof(ProjectedEvent),
                [
                    new FilterField(
                        "Flag",
                        typeof(bool),
                        FilterFieldKind.Scalar,
                        getter,
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(true)),
                ]));

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: 2);

    private static ItemUsedEvent Event(int characterId, int itemId, int quantity) =>
        new(Guid.NewGuid(), characterId, itemId, quantity);

    private sealed class MutableCriteria
    {
        public long CharacterId { get; set; }
    }

    private static async Task<TieredKernelSnapshot> WaitForSnapshotAsync(
        CompiledKernel kernel,
        Func<TieredKernelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            TieredKernelSnapshot? snapshot = kernel.TieredSnapshot;
            if (snapshot is not null && predicate(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered kernel did not reach expected state. Last snapshot: {kernel.TieredSnapshot}");
    }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Tiered;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CompilerCacheKeyRegressionTests
{
    [Fact]
    public void FilterCacheSeparatesTieredQueueCapacity()
    {
        FilterExpression filter = ItemIdEquals(741);

        CompiledKernel first = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered with { TieredPromotionQueueCapacity = 1 });
        CompiledKernel second = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered with { TieredPromotionQueueCapacity = 2 });

        Assert.NotSame(first, second);
        Assert.True(first.IsTiered);
        Assert.True(second.IsTiered);
    }

    [Fact]
    public void PipelineCacheUsesHotSinkReferenceIdentity()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)));

        var first = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            PipelineOptions(new ThrowOnceSink(throwFilters: false, throwProjections: false)));
        var second = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            PipelineOptions(new ThrowOnceSink(throwFilters: false, throwProjections: false)));

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task ThrowingHotSinkDoesNotWedgeFilterPromotion()
    {
        var sink = new ThrowOnceSink(throwFilters: true, throwProjections: false);
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            ItemIdEquals(91741),
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
                HotManifestSink = sink,
            });

        Assert.True(kernel.Matches(Event(itemId: 91741)));

        TieredKernelSnapshot snapshot = await WaitForSnapshotAsync(
            kernel,
            static item => item.Tier == TieredKernelTier.Compiled);

        Assert.False(snapshot.CompilationFailed);
        Assert.Equal(1, sink.FilterCalls);
        Assert.True(kernel.Matches(Event(itemId: 91741)));
    }

    [Fact]
    public async Task ThrowingHotSinkDoesNotWedgeProjectionPromotion()
    {
        var sink = new ThrowOnceSink(throwFilters: false, throwProjections: true);
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)),
            ProjectionRuntimeTestSupport.RejectInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
                HotManifestSink = sink,
            });

        ProjectedEvent interpreted = await projection.ProjectAsync(
            Event(itemId: 100),
            new object(),
            CancellationToken.None);
        Assert.Equal(100, interpreted.Field(nameof(ItemUsedEvent.ItemId)).Integer);

        TieredProjectionSnapshot snapshot = await ProjectionRuntimeTestSupport.WaitForSnapshotAsync(
            projection,
            static item => item.Tier == TieredProjectionTier.Compiled);

        Assert.False(snapshot.CompilationFailed);
        Assert.Equal(1, sink.ProjectionCalls);
        ProjectedEvent compiled = await projection.ProjectAsync(
            Event(itemId: 200),
            new object(),
            CancellationToken.None);
        Assert.Equal(200, compiled.Field(nameof(ItemUsedEvent.ItemId)).Integer);
    }

    private static FilterExpression ItemIdEquals(int itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId));

    private static EventPipelineCompilerOptions PipelineOptions(ITieredHotManifestSink sink) =>
        EventPipelineCompilerOptions.Tiered with
        {
            ProjectionOptions = ProjectionCompilerOptions.Tiered with { HotManifestSink = sink },
        };

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: 2);

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

    private sealed class ThrowOnceSink(
        bool throwFilters,
        bool throwProjections) : ITieredHotManifestSink
    {
        private int _filterCalls;
        private int _projectionCalls;

        public int FilterCalls => Volatile.Read(ref _filterCalls);
        public int ProjectionCalls => Volatile.Read(ref _projectionCalls);

        public void RecordHotFilter(
            Type subjectType,
            FilterExpression expression,
            long evaluations,
            long matches)
        {
            _ = subjectType;
            _ = expression;
            _ = evaluations;
            _ = matches;
            if (Interlocked.Increment(ref _filterCalls) == 1 && throwFilters)
                throw new InvalidOperationException("Transient hot sink failure.");
        }

        public void RecordHotProjection(
            Type subjectType,
            EventProjectionExpression projection,
            long materializations,
            long payloadWrites)
        {
            _ = subjectType;
            _ = projection;
            _ = materializations;
            _ = payloadWrites;
            if (Interlocked.Increment(ref _projectionCalls) == 1 && throwProjections)
                throw new InvalidOperationException("Transient hot sink failure.");
        }
    }
}

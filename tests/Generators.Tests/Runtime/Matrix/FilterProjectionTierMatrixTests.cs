using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterProjectionTierMatrixTests
{
    [Fact]
    public void FilterTiersReturnSameMatches()
    {
        FilterExpression filter = ItemIdEquals(100);

        foreach (TierKind tier in Enum.GetValues<TierKind>())
        {
            CompiledKernel kernel = CompileFilter(tier, filter);

            Assert.Equal(tier == TierKind.Interpreted, kernel.IsTiered);
            Assert.True(kernel.Matches(Event(itemId: 100, quantity: 2)));
            Assert.False(kernel.Matches(Event(itemId: 99, quantity: 2)));
        }
    }

    [Fact]
    public async Task ProjectionTiersReturnSameFields()
    {
        EventProjectionExpression projection = EventProjectionExpression.Select(
            nameof(ItemUsedEvent.Quantity));

        foreach (TierKind tier in Enum.GetValues<TierKind>())
        {
            CompiledProjection<object> compiled = CompileProjection(tier, projection);
            ProjectedEvent projected = await compiled.ProjectAsync(
                Event(itemId: 100, quantity: 3),
                new object(),
                CancellationToken.None);

            Assert.Equal(tier == TierKind.Interpreted, compiled.IsTiered);
            Assert.True(projected.TryGetField(nameof(ItemUsedEvent.Quantity), out var quantity));
            Assert.Equal(3, quantity.Integer);
            Assert.False(projected.TryGetField(nameof(ItemUsedEvent.ItemId), out _));
        }
    }

    [Fact]
    public async Task PipelineTiersReturnSameProjectedResult()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendSourceFilter(ItemIdEquals(100))
            .AppendProjection(EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)));

        foreach (TierKind tier in Enum.GetValues<TierKind>())
        {
            CompiledEventPipeline<object> compiled = CompilePipeline(tier, pipeline);

            ProjectedEvent? rejected = await compiled.ProjectAsync(
                Event(itemId: 99, quantity: 1),
                new object(),
                CancellationToken.None);
            ProjectedEvent? projected = await compiled.ProjectAsync(
                Event(itemId: 100, quantity: 4),
                new object(),
                CancellationToken.None);

            Assert.Null(rejected);
            Assert.NotNull(projected);
            Assert.Equal(4, projected!.Field(nameof(ItemUsedEvent.Quantity)).Integer);
            Assert.False(projected.TryGetField(nameof(ItemUsedEvent.ItemId), out _));
        }
    }

    [Fact]
    public void InvalidExpressionsFailForEveryTier()
    {
        FilterExpression invalidFilter = FilterExpression.Compare(
            "Missing",
            FilterOperator.Equal,
            FilterValue.From(100L));
        EventProjectionExpression invalidProjection = EventProjectionExpression.Select("Missing");
        EventPipelineExpression invalidPipeline = EventPipelineExpression.Default
            .AppendSourceFilter(invalidFilter)
            .AppendProjection(EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)));

        foreach (TierKind tier in Enum.GetValues<TierKind>())
        {
            Assert.Throws<FilterValidationException>(() => CompileFilter(tier, invalidFilter));
            Assert.Throws<FilterValidationException>(() => CompileProjection(tier, invalidProjection));
            Assert.Throws<FilterValidationException>(() => CompilePipeline(tier, invalidPipeline));
        }
    }

    private static CompiledKernel CompileFilter(TierKind tier, FilterExpression filter)
    {
        if (tier != TierKind.HotProvider)
            return FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterOptions(tier));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new MatrixProvider());
        return FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterOptions(tier));
    }

    private static CompiledProjection<object> CompileProjection(
        TierKind tier,
        EventProjectionExpression projection)
    {
        if (tier != TierKind.HotProvider)
        {
            return ProjectionCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                projection,
                ProjectionRuntimeTestSupport.RejectInclude,
                ProjectionOptions(tier));
        }

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new MatrixProvider());
        return ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            ProjectionRuntimeTestSupport.RejectInclude,
            ProjectionOptions(tier));
    }

    private static CompiledEventPipeline<object> CompilePipeline(
        TierKind tier,
        EventPipelineExpression pipeline)
    {
        if (tier != TierKind.HotProvider)
        {
            return EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                ProjectionRuntimeTestSupport.RejectInclude,
                PipelineOptions(tier));
        }

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new MatrixProvider());
        return EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            PipelineOptions(tier));
    }

    private static FilterCompilerOptions FilterOptions(TierKind tier) =>
        tier == TierKind.Immediate
            ? FilterCompilerOptions.Immediate
            : FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumEvaluations = int.MaxValue,
            };

    private static ProjectionCompilerOptions ProjectionOptions(TierKind tier) =>
        tier == TierKind.Immediate
            ? ProjectionCompilerOptions.Immediate
            : ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumOperations = int.MaxValue,
            };

    private static EventPipelineCompilerOptions PipelineOptions(TierKind tier) =>
        new()
        {
            FilterOptions = FilterOptions(tier),
            ProjectionOptions = ProjectionOptions(tier),
        };

    private static FilterExpression ItemIdEquals(int itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId));

    private static ItemUsedEvent Event(int itemId, int quantity) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: quantity);

    private enum TierKind
    {
        Interpreted,
        Immediate,
        HotProvider,
    }

    private sealed class MatrixProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(
            Type subjectType,
            string fingerprint,
            out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = static subject => ((ItemUsedEvent)subject).ItemId == 100;
            return true;
        }

        public bool TryGetProjection(
            Type subjectType,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = static subject =>
            [
                new ProjectedEventField(
                    nameof(ItemUsedEvent.Quantity),
                    ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).Quantity)),
            ];
            return true;
        }
    }
}

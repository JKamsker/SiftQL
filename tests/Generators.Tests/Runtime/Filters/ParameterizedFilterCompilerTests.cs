using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Parameterized;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Tiered;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ParameterizedFilterCompilerTests : IDisposable
{
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

    public void Dispose() =>
        ParameterizedFilterPlanCache.ClearForTests();

    private static FilterExpression ItemIdEquals(int itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId) with { ParameterKey = "p0" });

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

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Generators.Tests;

public sealed class TieredFilterCacheRegressionTests
{
    [Fact]
    public void TieredCompileReturnsFreshMutableStatePerCall()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        FilterCompilerOptions options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.Zero,
            TieredPromotionMinimumEvaluations = 2,
        };

        CompiledKernel first = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, options);
        Assert.True(first.Matches(Event(100)));

        CompiledKernel second = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, options);

        Assert.NotSame(first, second);
        Assert.Equal(0L, second.TieredSnapshot?.Evaluations);
        Assert.True(second.Matches(Event(100)));
        Assert.False(second.TieredSnapshot?.CompilationQueued ?? true);
    }

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: 2);
}

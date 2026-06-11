using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Parameterized;

namespace SiftQL.Generators.Tests;

public sealed class InactiveFilterParameterPayloadRegressionTests : IDisposable
{
    public InactiveFilterParameterPayloadRegressionTests() =>
        ParameterizedFilterPlanCache.ClearForTests();

    [Fact]
    public void InactivePayloadParametersDoNotPoisonPlanCache()
    {
        FilterExpression first = ExistsWithInactiveParameter("p0");
        FilterExpression second = ExistsWithInactiveParameter("p1");

        CompiledKernel firstKernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            first,
            FilterCompilerOptions.Immediate);
        CompiledKernel secondKernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            second,
            FilterCompilerOptions.Immediate);

        Assert.True(firstKernel.Matches(Event()));
        Assert.True(secondKernel.Matches(Event()));
        Assert.Equal(0, ParameterizedFilterPlanCache.Snapshot.Count);
    }

    private static FilterExpression ExistsWithInactiveParameter(string key) =>
        FilterExpression.Exists(nameof(ItemUsedEvent.ItemId)) with
        {
            Value = FilterValue.From(1L) with { ParameterKey = key },
        };

    private static ItemUsedEvent Event() =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: 100, Quantity: 2);

    public void Dispose() =>
        ParameterizedFilterPlanCache.ClearForTests();
}

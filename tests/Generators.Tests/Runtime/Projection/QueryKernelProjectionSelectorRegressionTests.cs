using SiftQL.Translation;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectionSelectorRegressionTests
{
    [Fact]
    public void StaticMemberProjectionSelectorThrowsKernelExpressionException()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<ItemUsedEvent>()
                .Select(static (_, _) => new { Value = StaticProjectionValue }));
    }

    private static int StaticProjectionValue => 42;
}

using SiftQL.Compiler;
using SiftQL.Kernel;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextStringRegressionTests
{
    [Fact]
    public void ContextWhereWithUnusedContextSupportsSourceStringPrefix()
    {
        QueryKernel<ContextStringEvent, EmptyContext> query = QueryKernel
            .For<ContextStringEvent, EmptyContext>()
            .Where(static (ev, _) => ev.Name.StartsWith("orders/", StringComparison.Ordinal));
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ContextStringEvent),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ContextStringEvent("orders/1001")));
        Assert.False(kernel.Matches(new ContextStringEvent("inventory/1001")));
    }

    private sealed record ContextStringEvent(string Name) : IFilterSubject;
    private sealed class EmptyContext;
}

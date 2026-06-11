using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextCollectionRegressionTests
{
    [Fact]
    public void ContextWhereWithUnusedContextSupportsSourceAny()
    {
        FilterSchema.RegisterValueObject(typeof(ContextLootItem));

        QueryKernel<ContextLootBag, EmptyContext> query = QueryKernel
            .For<ContextLootBag, EmptyContext>()
            .Where(static (bag, _) => bag.Items.Any(item => item.Name == "Excalibur"));
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ContextLootBag),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ContextLootBag([new("Excalibur")])));
        Assert.False(kernel.Matches(new ContextLootBag([new("Potion")])));
    }

    [Fact]
    public void ContextWhereWithUnusedContextSupportsSourceAll()
    {
        FilterSchema.RegisterValueObject(typeof(ContextLootItem));

        QueryKernel<ContextLootBag, EmptyContext> query = QueryKernel
            .For<ContextLootBag, EmptyContext>()
            .Where(static (bag, _) => bag.Items.All(item => item.Equipped));
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ContextLootBag),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ContextLootBag([new("Sword", Equipped: true)])));
        Assert.False(kernel.Matches(new ContextLootBag(
        [
            new("Sword", Equipped: true),
            new("Shield", Equipped: false),
        ])));
    }

    private sealed record ContextLootBag(ContextLootItem[] Items) : IFilterSubject;
    private sealed record ContextLootItem(string Name, bool Equipped = false);
    private sealed class EmptyContext;
}

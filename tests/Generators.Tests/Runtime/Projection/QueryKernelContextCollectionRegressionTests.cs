using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
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

    [Fact]
    public async Task ContextWhereWithContextIncludeSupportsSourceAny()
    {
        FilterSchema.RegisterValueObject(typeof(ContextLootItem));

        QueryKernel<ContextLootBag, LootContext> query = QueryKernel
            .For<ContextLootBag, LootContext>()
            .Where(static (bag, context) =>
                bag.Items.Any(item => item.Name == "Excalibur") &&
                context.Enabled());
        CompiledEventPipeline<LootContext> compiled = EventPipelineCompiler.Compile<LootContext>(
            typeof(ContextLootBag),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new ContextLootBag([new("Excalibur")]),
            new LootContext(enabled: true),
            CancellationToken.None);
        ProjectedEvent? wrongItem = await compiled.ProjectAsync(
            new ContextLootBag([new("Potion")]),
            new LootContext(enabled: true),
            CancellationToken.None);
        ProjectedEvent? disabled = await compiled.ProjectAsync(
            new ContextLootBag([new("Excalibur")]),
            new LootContext(enabled: false),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(wrongItem);
        Assert.Null(disabled);
    }

    private sealed record ContextLootBag(ContextLootItem[] Items) : IFilterSubject;
    private sealed record ContextLootItem(string Name, bool Equipped = false);
    private sealed class EmptyContext;
    private sealed record LootContext(bool enabled)
    {
        public bool Enabled() => enabled;
    }
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelElementMatchTranslationTests
{
    [Fact]
    public void CorrelatedAndMatchesSameElement()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootBag> query = QueryKernel.For<LootBag>()
            .Where(static e => e.Items.Any(i => i.Name == "Excalibur" && i.Equipped));

        Assert.Equal(FilterExpressionKind.ElemMatch, query.Filter.Kind);

        var kernel = FilterCompiler.Compile(typeof(LootBag), query.Filter, FilterCompilerOptions.Immediate);

        // One element is both Excalibur AND equipped.
        Assert.True(kernel.Matches(new LootBag([new("Excalibur", true, 0)])));
        Assert.True(kernel.Matches(new LootBag([new("Excalibur", true, 0), new("Shield", false, 0)])));

        // The trap: Excalibur exists and an equipped item exists, but no single
        // item is both. Decorrelated Any-and-Any would wrongly match this.
        Assert.False(kernel.Matches(new LootBag([new("Excalibur", false, 0), new("Shield", true, 0)])));
        Assert.False(kernel.Matches(new LootBag([])));
    }

    [Fact]
    public void CorrelatedAndWithOrderingMatchesSameElement()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootBag> query = QueryKernel.For<LootBag>()
            .Where(static e => e.Items.Any(i => i.Name == "Sword" && i.Power > 10));

        var kernel = FilterCompiler.Compile(typeof(LootBag), query.Filter, FilterCompilerOptions.Immediate);
        var tiered = FilterCompiler.Compile(typeof(LootBag), query.Filter, FilterCompilerOptions.Tiered);

        Assert.True(kernel.Matches(new LootBag([new("Sword", false, 50)])));
        Assert.False(kernel.Matches(new LootBag([new("Sword", false, 5), new("Axe", false, 99)])));
        Assert.True(tiered.Matches(new LootBag([new("Sword", false, 50)])));
        Assert.False(tiered.Matches(new LootBag([new("Sword", false, 5), new("Axe", false, 99)])));
    }

    [Fact]
    public void SingleConditionAnyStaysDecorrelatedContains()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootBag> query = QueryKernel.For<LootBag>()
            .Where(static e => e.Items.Any(i => i.Name == "Excalibur"));

        // Single-condition Any keeps the existing decorrelated Contains shape.
        Assert.Equal(FilterExpressionKind.Contains, query.Filter.Kind);
    }

    [Fact]
    public void SingleOrderingAnyLowersToElemMatch()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootBag> query = QueryKernel.For<LootBag>()
            .Where(static e => e.Items.Any(i => i.Power > 10));

        Assert.Equal(FilterExpressionKind.ElemMatch, query.Filter.Kind);

        var kernel = FilterCompiler.Compile(typeof(LootBag), query.Filter, FilterCompilerOptions.Immediate);
        var tiered = FilterCompiler.Compile(typeof(LootBag), query.Filter, FilterCompilerOptions.Tiered);

        Assert.True(kernel.Matches(new LootBag([new("Axe", false, 11)])));
        Assert.False(kernel.Matches(new LootBag([new("Axe", false, 10)])));
        Assert.True(tiered.Matches(new LootBag([new("Axe", false, 11)])));
        Assert.False(tiered.Matches(new LootBag([new("Axe", false, 10)])));
    }

    [Fact]
    public void NestedCorrelatedAnyLowersToNestedElemMatch()
    {
        FilterSchema.RegisterValueObject(typeof(LootGroup));
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<GroupedLootBag> query = QueryKernel.For<GroupedLootBag>()
            .Where(static e => e.Groups.Any(g =>
                g.Items.Any(i => i.Name == "Sword" && i.Power > 10)));

        Assert.Equal(FilterExpressionKind.ElemMatch, query.Filter.Kind);

        var kernel = FilterCompiler.Compile(
            typeof(GroupedLootBag),
            query.Filter,
            FilterCompilerOptions.Immediate);
        var tiered = FilterCompiler.Compile(
            typeof(GroupedLootBag),
            query.Filter,
            FilterCompilerOptions.Tiered);

        Assert.True(kernel.Matches(new GroupedLootBag(
        [
            new LootGroup([new("Sword", false, 11)]),
        ])));
        Assert.False(kernel.Matches(new GroupedLootBag(
        [
            new LootGroup([new("Sword", false, 5), new("Axe", false, 99)]),
        ])));
        Assert.True(tiered.Matches(new GroupedLootBag(
        [
            new LootGroup([new("Sword", false, 11)]),
        ])));
        Assert.False(tiered.Matches(new GroupedLootBag(
        [
            new LootGroup([new("Sword", false, 5), new("Axe", false, 99)]),
        ])));
    }

    private sealed record LootBag(LootItem[] Items) : IFilterSubject;
    private sealed record GroupedLootBag(LootGroup[] Groups) : IFilterSubject;
    private sealed record LootGroup(LootItem[] Items);
    private sealed record LootItem(string? Name, bool Equipped, int Power);
}

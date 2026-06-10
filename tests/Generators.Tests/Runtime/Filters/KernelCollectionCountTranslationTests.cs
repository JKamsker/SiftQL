using System.Collections.Generic;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelCollectionCountTranslationTests
{
    [Fact]
    public void WhereCountMethodOnArrayProducesCountNode()
    {
        QueryKernel<Cart> query = QueryKernel.For<Cart>()
            .Where(static c => c.Tags.Count() > 1);

        Assert.Equal(FilterExpressionKind.Count, query.Filter.Kind);
        Assert.Equal(nameof(Cart.Tags), query.Filter.Field);
        Assert.Equal(FilterOperator.GreaterThan, query.Filter.Operator);

        AssertFilter(
            query.Filter,
            new FilterCase(new Cart(["a", "b"], []), true),
            new FilterCase(new Cart(["a"], []), false),
            new FilterCase(new Cart([], []), false));
    }

    [Fact]
    public void WhereLengthOnArrayProducesCountNode()
    {
        QueryKernel<Cart> query = QueryKernel.For<Cart>()
            .Where(static c => c.Tags.Length == 0);

        Assert.Equal(FilterExpressionKind.Count, query.Filter.Kind);

        AssertFilter(
            query.Filter,
            new FilterCase(new Cart([], []), true),
            new FilterCase(new Cart(["a"], []), false));
    }

    [Fact]
    public void WhereCountPropertyOnListProducesCountNode()
    {
        QueryKernel<Cart> query = QueryKernel.For<Cart>()
            .Where(static c => c.Quantities.Count >= 2);

        Assert.Equal(FilterExpressionKind.Count, query.Filter.Kind);
        Assert.Equal(nameof(Cart.Quantities), query.Filter.Field);

        AssertFilter(
            query.Filter,
            new FilterCase(new Cart([], [1, 2]), true),
            new FilterCase(new Cart([], [1]), false));
    }

    [Fact]
    public void CountFactoryCompilesAndMatches()
    {
        FilterExpression filter = FilterExpression.Count(
            nameof(Cart.Tags),
            FilterOperator.GreaterThanOrEqual,
            FilterValue.From(2L));

        AssertFilter(
            filter,
            new FilterCase(new Cart(["a", "b"], []), true),
            new FilterCase(new Cart(["a"], []), false));
    }

    [Fact]
    public void WhereCountMethodOnObjectCollectionCompilesAndMatches()
    {
        FilterSchema.RegisterValueObject(typeof(CountLootItem));
        QueryKernel<LootCart> query = QueryKernel.For<LootCart>()
            .Where(static cart => cart.Items.Count() > 0);

        Assert.Equal(FilterExpressionKind.Count, query.Filter.Kind);
        Assert.Equal(nameof(LootCart.Items), query.Filter.Field);

        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(LootCart),
            query.Filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(LootCart),
            query.Filter,
            FilterCompilerOptions.Tiered);

        Assert.True(immediate.Matches(new LootCart([new CountLootItem("Sword")])));
        Assert.False(immediate.Matches(new LootCart([])));
        Assert.True(tiered.Matches(new LootCart([new CountLootItem("Sword")])));
        Assert.False(tiered.Matches(new LootCart([])));
    }

    [Fact]
    public void CountOnScalarFieldIsRejected()
    {
        FilterExpression filter = FilterExpression.Count(
            nameof(Cart.Name),
            FilterOperator.GreaterThan,
            FilterValue.From(0L));

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(Cart), filter, FilterCompilerOptions.Immediate));
    }

    private static void AssertFilter(FilterExpression filter, params FilterCase[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(typeof(Cart), filter, FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(typeof(Cart), filter, FilterCompilerOptions.Tiered);
        foreach (FilterCase item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

    private sealed record Cart(string[] Tags, List<int> Quantities, string Name = "") : IFilterSubject;
    private sealed record LootCart(CountLootItem[] Items) : IFilterSubject;
    private sealed record CountLootItem(string Name);
    private sealed record FilterCase(Cart Subject, bool Expected);
}

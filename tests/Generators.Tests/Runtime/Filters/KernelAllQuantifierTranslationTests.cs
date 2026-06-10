using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelAllQuantifierTranslationTests
{
    [Fact]
    public void WhereAllBooleanMemberMatchesWhenEveryElementSatisfies()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootEvent> query = QueryKernel.For<LootEvent>()
            .Where(static subject => subject.Items.All(item => item.Equipped));

        // All(p) lowers to Not(Any(Not p)).
        Assert.Equal(FilterExpressionKind.Not, query.Filter.Kind);

        var kernel = FilterCompiler.Compile(typeof(LootEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new LootEvent([new("a", true), new("b", true)])));
        Assert.False(kernel.Matches(new LootEvent([new("a", true), new("b", false)])));
    }

    [Fact]
    public void AllOverEmptyCollectionIsVacuouslyTrue()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        QueryKernel<LootEvent> query = QueryKernel.For<LootEvent>()
            .Where(static subject => subject.Items.All(item => item.Equipped));

        var kernel = FilterCompiler.Compile(typeof(LootEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new LootEvent([])));
    }

    [Fact]
    public void AllWithUnsupportedElementPredicateThrows()
    {
        FilterSchema.RegisterValueObject(typeof(LootItem));

        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<LootEvent>()
                .Where(static subject => subject.Items.All(item => item.Name == "x")));
    }

    private sealed record LootEvent(LootItem[] Items) : IFilterSubject;
    private sealed record LootItem(string? Name, bool Equipped);
}

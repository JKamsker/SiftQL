using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ElemMatchInterfaceCollectionRegressionTests
{
    [Fact]
    public void ElemMatchResolvesCollectionsInheritedThroughInterfaces()
    {
        FilterSchema.RegisterValueObject(typeof(InterfaceLootItem));

        QueryKernel<IDerivedLootBag> query = QueryKernel.For<IDerivedLootBag>()
            .Where(static bag => bag.Items.Any(item => item.Name == "Excalibur" && item.Equipped));

        Assert.Equal(FilterExpressionKind.ElemMatch, query.Filter.Kind);
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(IDerivedLootBag),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new InterfaceLootBag([new("Excalibur", true)])));
        Assert.False(kernel.Matches(new InterfaceLootBag([new("Excalibur", false), new("Shield", true)])));
    }

    private interface IBaseLootBag
    {
        InterfaceLootItem[] Items { get; }
    }

    private interface IDerivedLootBag : IBaseLootBag, IFilterSubject;

    private sealed record InterfaceLootBag(InterfaceLootItem[] Items) : IDerivedLootBag;

    private sealed record InterfaceLootItem(string Name, bool Equipped);
}

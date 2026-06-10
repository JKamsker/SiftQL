using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelNestedLinqTranslationTests
{
    [Fact]
    public void WhereAnyOverObjectCollectionMemberEqualsCompilesToContains()
    {
        RegisterInventoryValueObjects();

        QueryKernel<InventoryEvent> query = QueryKernel.For<InventoryEvent>()
            .Where(static subject => subject.Items.Any(item => item.Name == "Destroyer"));

        AssertContains(query.Filter, "Items.Name", "Destroyer");
        var kernel = FilterCompiler.Compile(typeof(InventoryEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new InventoryEvent([new("Destroyer", [], Equipped: false)])));
        Assert.False(kernel.Matches(new InventoryEvent([new("Cruiser", [], Equipped: false)])));
    }

    [Fact]
    public void WhereNestedAnyOverObjectCollectionsFlattensFieldPath()
    {
        RegisterInventoryValueObjects();

        QueryKernel<GroupedInventoryEvent> query = QueryKernel.For<GroupedInventoryEvent>()
            .Where(static subject => subject.Groups.Any(group =>
                group.Items.Any(item => item.Name == "Destroyer")));

        AssertContains(query.Filter, "Groups.Items.Name", "Destroyer");
        var kernel = FilterCompiler.Compile(typeof(GroupedInventoryEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new GroupedInventoryEvent([new([new("Destroyer", [], Equipped: false)])])));
        Assert.False(kernel.Matches(new GroupedInventoryEvent([new([new("Cruiser", [], Equipped: false)])])));
    }

    [Fact]
    public void WhereAnyOverScalarCollectionElementEqualsUsesCollectionField()
    {
        QueryKernel<TagEvent> query = QueryKernel.For<TagEvent>()
            .Where(static subject => subject.Tags.Any(tag => tag == "rare"));

        AssertContains(query.Filter, nameof(TagEvent.Tags), "rare");
        var kernel = FilterCompiler.Compile(typeof(TagEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new TagEvent(["rare"])));
        Assert.False(kernel.Matches(new TagEvent(["common"])));
    }

    [Fact]
    public void WhereAnyOverNestedScalarCollectionContainsFlattensFieldPath()
    {
        RegisterInventoryValueObjects();

        QueryKernel<InventoryEvent> query = QueryKernel.For<InventoryEvent>()
            .Where(static subject => subject.Items.Any(item => item.Tags.Contains("rare")));

        AssertContains(query.Filter, "Items.Tags", "rare");
        var kernel = FilterCompiler.Compile(typeof(InventoryEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new InventoryEvent([new("Cruiser", ["rare"], Equipped: false)])));
        Assert.False(kernel.Matches(new InventoryEvent([new("Cruiser", ["common"], Equipped: false)])));
    }

    [Fact]
    public void WhereAnyWithValueListContainsExpandsToContainsOr()
    {
        RegisterInventoryValueObjects();
        string[] accepted = ["Destroyer", "Cruiser"];

        QueryKernel<InventoryEvent> query = QueryKernel.For<InventoryEvent>()
            .Where(subject => subject.Items.Any(item => accepted.Contains(item.Name)));

        Assert.Equal(FilterExpressionKind.Or, query.Filter.Kind);
        Assert.All(query.Filter.Children, child => Assert.Equal("Items.Name", child.Field));

        var kernel = FilterCompiler.Compile(typeof(InventoryEvent), query.Filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new InventoryEvent([new("Cruiser", [], Equipped: false)])));
        Assert.False(kernel.Matches(new InventoryEvent([new("Frigate", [], Equipped: false)])));
    }

    [Fact]
    public void WhereAnyBooleanElementMemberUsesBooleanContains()
    {
        RegisterInventoryValueObjects();

        QueryKernel<InventoryEvent> query = QueryKernel.For<InventoryEvent>()
            .Where(static subject => subject.Items.Any(item => !item.Equipped));

        AssertContains(query.Filter, "Items.Equipped", false);
        var kernel = FilterCompiler.Compile(typeof(InventoryEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new InventoryEvent([new("Destroyer", [], Equipped: false)])));
        Assert.False(kernel.Matches(new InventoryEvent([new("Destroyer", [], Equipped: true)])));
    }

    [Fact]
    public void WhereCorrelatedAndPredicateLowersToElemMatch()
    {
        RegisterInventoryValueObjects();

        QueryKernel<InventoryEvent> query = QueryKernel.For<InventoryEvent>()
            .Where(static subject => subject.Items.Any(item =>
                item.Name == "Destroyer" && item.Equipped));

        Assert.Equal(FilterExpressionKind.ElemMatch, query.Filter.Kind);
        var kernel = FilterCompiler.Compile(typeof(InventoryEvent), query.Filter, FilterCompilerOptions.Immediate);

        // Both conditions satisfied by the same element.
        Assert.True(kernel.Matches(new InventoryEvent([new("Destroyer", [], Equipped: true)])));
        // Conditions satisfied by different elements must NOT match.
        Assert.False(kernel.Matches(new InventoryEvent(
            [new("Destroyer", [], Equipped: false), new("Cruiser", [], Equipped: true)])));
    }

    private static void AssertContains(FilterExpression expression, string field, string value)
    {
        Assert.Equal(FilterExpressionKind.Contains, expression.Kind);
        Assert.Equal(field, expression.Field);
        Assert.Equal(value, expression.Value?.String);
    }

    private static void AssertContains(FilterExpression expression, string field, bool value)
    {
        Assert.Equal(FilterExpressionKind.Contains, expression.Kind);
        Assert.Equal(field, expression.Field);
        Assert.Equal(value, expression.Value?.Boolean);
    }

    private static void RegisterInventoryValueObjects()
    {
        FilterSchema.RegisterValueObject(typeof(InventoryGroup));
        FilterSchema.RegisterValueObject(typeof(InventoryItem));
    }

    private sealed record InventoryEvent(InventoryItem[] Items) : IFilterSubject;

    private sealed record GroupedInventoryEvent(InventoryGroup[] Groups) : IFilterSubject;

    private sealed record InventoryGroup(InventoryItem[] Items);

    private sealed record InventoryItem(string? Name, string[] Tags, bool Equipped);

    private sealed record TagEvent(string[] Tags) : IFilterSubject;
}

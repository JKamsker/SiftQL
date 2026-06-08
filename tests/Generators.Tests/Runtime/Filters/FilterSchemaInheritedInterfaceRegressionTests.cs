using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaInheritedInterfaceRegressionTests
{
    [Fact]
    public void InterfaceSchemaIncludesInheritedInterfaceProperties()
    {
        FilterSchema schema = FilterSchema.For(typeof(IZoneRegionEvent));

        Assert.True(schema.TryGetField(nameof(IBaseRegionEvent.Region), out _));
        Assert.True(schema.TryGetField(nameof(IZoneRegionEvent.Zone), out _));
    }

    [Fact]
    public void InterfaceFilterMatchesInheritedInterfaceProperty()
    {
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(IZoneRegionEvent),
            FilterExpression.Compare(
                nameof(IBaseRegionEvent.Region),
                FilterOperator.Equal,
                FilterValue.From("north")));

        Assert.True(kernel.Matches(new ZoneRegionEvent("north", 7)));
    }

    private interface IBaseRegionEvent : IFilterSubject
    {
        string Region { get; }
    }

    private interface IZoneRegionEvent : IBaseRegionEvent
    {
        int Zone { get; }
    }

    private sealed record ZoneRegionEvent(string Region, int Zone) : IZoneRegionEvent;
}

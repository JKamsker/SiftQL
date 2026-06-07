using SiftQL.Parameterized;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ParameterizedFilterPlanTests
{
    [Fact]
    public void ConstantFilterPlanNode_True()
    {
        var node = new ConstantFilterPlanNode(true);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void ConstantFilterPlanNode_False()
    {
        var node = new ConstantFilterPlanNode(false);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_AndAllMatch()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(true),
            new ConstantFilterPlanNode(true),
        };
        var node = new CompositeFilterPlanNode(children, and: true);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_AndOneFails()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(true),
            new ConstantFilterPlanNode(false),
        };
        var node = new CompositeFilterPlanNode(children, and: true);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_OrOneMatches()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(false),
            new ConstantFilterPlanNode(true),
        };
        var node = new CompositeFilterPlanNode(children, and: false);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_OrNoneMatch()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(false),
            new ConstantFilterPlanNode(false),
        };
        var node = new CompositeFilterPlanNode(children, and: false);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }
}

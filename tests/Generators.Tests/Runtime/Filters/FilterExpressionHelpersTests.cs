using SiftQL.Compiler;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionHelpersTests
{
    [Fact]
    public void NumberIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.NumberIn(3.14, [1.0, 2.0, 3.14]));
    }

    [Fact]
    public void NumberIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.NumberIn(9.0, [1.0, 2.0, 3.0]));
    }

    [Fact]
    public void NumberIn_EmptyArray()
    {
        Assert.False(FilterExpressionHelpers.NumberIn(1.0, []));
    }

    [Fact]
    public void StringIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.StringIn("b", ["a", "b", "c"], false));
    }

    [Fact]
    public void StringIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.StringIn("z", ["a", "b"], false));
    }

    [Fact]
    public void StringIn_NullActualWithHasNull()
    {
        Assert.True(FilterExpressionHelpers.StringIn(null, ["a"], true));
    }

    [Fact]
    public void StringIn_NullActualWithoutHasNull()
    {
        Assert.False(FilterExpressionHelpers.StringIn(null, ["a"], false));
    }

    [Fact]
    public void GuidIn_MatchesValue()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterExpressionHelpers.GuidIn(g, [Guid.Empty, g]));
    }

    [Fact]
    public void GuidIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.GuidIn(Guid.NewGuid(), [Guid.Empty]));
    }

    [Fact]
    public void EnumIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.EnumIn(2L, [1L, 2L, 3L]));
    }

    [Fact]
    public void EnumIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.EnumIn(5L, [1L, 2L, 3L]));
    }
}

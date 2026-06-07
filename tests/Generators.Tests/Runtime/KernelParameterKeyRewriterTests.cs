using SiftQL;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelParameterKeyRewriterTests
{
    [Fact]
    public void ParameterCount_NoParameters_ReturnsZero()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Equal(0, KernelParameterKeyRewriter.ParameterCount(expr));
    }

    [Fact]
    public void ParameterCount_WithParameters_CountsDistinct()
    {
        var expr = FilterExpression.And(
            FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p0" }),
            FilterExpression.Compare("Quantity", FilterOperator.Equal,
                FilterValue.From(2L) with { ParameterKey = "p1" }));
        Assert.Equal(2, KernelParameterKeyRewriter.ParameterCount(expr));
    }

    [Fact]
    public void ParameterCount_Projection_CountsIncludeArguments()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [EventProjectionArgument.From("limit", 10L) with
                {
                    Value = FilterValue.From(10L) with { ParameterKey = "p0" },
                }]),
        ]);
        Assert.Equal(1, KernelParameterKeyRewriter.ParameterCount(projection));
    }

    [Fact]
    public void ParameterCount_Pipeline_CountsBothFilterAndProjectionKeys()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p0" }))
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude("test.intrinsic", "result",
                    [EventProjectionArgument.From("limit", 10L) with
                    {
                        Value = FilterValue.From(10L) with { ParameterKey = "p1" },
                    }]),
            ]));
        Assert.Equal(2, KernelParameterKeyRewriter.ParameterCount(pipeline));
    }

    [Fact]
    public void ParameterOffset_ReturnsNextAvailableOffset()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p2" }));
        Assert.Equal(3, KernelParameterKeyRewriter.ParameterOffset(pipeline));
    }

    [Fact]
    public void ParameterOffset_NonNumericKeys_IgnoredInOffset()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "custom" }));
        Assert.Equal(0, KernelParameterKeyRewriter.ParameterOffset(pipeline));
    }

    [Fact]
    public void Rebase_FilterExpression_ShiftsParameterKeys()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 5);
        Assert.Equal("p5", rebased.Value?.ParameterKey);
    }

    [Fact]
    public void Rebase_FilterExpression_ZeroOffset_ReturnsSame()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 0);
        Assert.Same(expr, rebased);
    }

    [Fact]
    public void Rebase_FilterExpression_NoParameters_ReturnsSame()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 5);
        Assert.Same(expr, rebased);
    }

    [Fact]
    public void Rebase_ProjectionExpression_ShiftsParameterKeys()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [new EventProjectionArgument("limit",
                    FilterValue.From(10L) with { ParameterKey = "p0" })]),
        ]);
        EventProjectionExpression rebased = KernelParameterKeyRewriter.Rebase(projection, 3);
        Assert.Equal("p3", rebased.Includes[0].Arguments[0].Value.ParameterKey);
    }

    [Fact]
    public void Rebase_ProjectionExpression_ZeroOffset_ReturnsSame()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [new EventProjectionArgument("limit",
                    FilterValue.From(10L) with { ParameterKey = "p0" })]),
        ]);
        EventProjectionExpression rebased = KernelParameterKeyRewriter.Rebase(projection, 0);
        Assert.Same(projection, rebased);
    }
}

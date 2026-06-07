using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CompilerOptionsValidationTests
{
    [Fact]
    public void FilterOptions_Immediate_ReturnsDefaultPolicy()
    {
        var options = FilterCompilerOptions.Immediate;
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var policy = options.CreateFilterPromotionPolicy(expr);
        Assert.Equal(0, policy.MinimumEvaluations);
    }

    [Fact]
    public void FilterOptions_NegativeAge_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromSeconds(-1),
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void FilterOptions_ZeroEvaluations_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumEvaluations = 0,
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void FilterOptions_ZeroQueueCapacity_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionQueueCapacity = 0,
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void ProjectionOptions_Immediate_ReturnsDefaultPolicy()
    {
        var policy = ProjectionCompilerOptions.Immediate.CreatePromotionPolicy();
        Assert.Equal(0, policy.MinimumOperations);
    }

    [Fact]
    public void ProjectionOptions_NegativeAge_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromSeconds(-1),
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }

    [Fact]
    public void ProjectionOptions_ZeroOperations_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionMinimumOperations = 0,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }

    [Fact]
    public void ProjectionOptions_ZeroQueueCapacity_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionQueueCapacity = 0,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }
}

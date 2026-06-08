using SiftQL.Compiler;
using SiftQL.Expressions;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionShapeRegressionTests
{
    [Fact]
    public void FilterWithNullChildrenThrowsValidationException()
    {
        var filter = new FilterExpression(FilterExpressionKind.And) { Children = null! };

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), filter));
    }

    [Fact]
    public void FilterWithNullValueArrayThrowsValidationException()
    {
        var filter = new FilterExpression(FilterExpressionKind.In)
        {
            Field = nameof(ItemUsedEvent.ItemId),
            Values = null!,
        };

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), filter));
    }

    [Fact]
    public void FilterWithNullValueArrayEntryThrowsValidationException()
    {
        var filter = new FilterExpression(FilterExpressionKind.In)
        {
            Field = nameof(ItemUsedEvent.ItemId),
            Values = [null!],
        };

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), filter));
    }
}

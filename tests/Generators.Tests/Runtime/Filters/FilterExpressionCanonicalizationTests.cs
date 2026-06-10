using System.Collections.Generic;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionCanonicalizationTests
{
    [Fact]
    public void StructuralComparerEqualsCompositesBuiltTwice()
    {
        var a = FilterExpression.In("region", [FilterValue.From("EU")]);
        var b = FilterExpression.In("region", [FilterValue.From("EU")]);

        Assert.NotEqual(a, b); // default record equality compares arrays by reference
        Assert.True(FilterExpression.StructuralComparer.Equals(a, b));
        Assert.Equal(
            FilterExpression.StructuralComparer.GetHashCode(a),
            FilterExpression.StructuralComparer.GetHashCode(b));
    }

    [Fact]
    public void HashSetWithStructuralComparerDedupes()
    {
        FilterExpression Build() => FilterExpression.And(
            FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")),
            FilterExpression.Compare("total", FilterOperator.GreaterThan, FilterValue.From(100.0)));

        var set = new HashSet<FilterExpression>(FilterExpression.StructuralComparer) { Build() };
        Assert.Contains(Build(), set);
    }

    [Fact]
    public void CanonicalFormIsOrderIndependentForAnd()
    {
        var p = FilterExpression.Compare("a", FilterOperator.Equal, FilterValue.From(1L));
        var q = FilterExpression.Compare("b", FilterOperator.Equal, FilterValue.From(2L));

        var x = FilterExpression.And(p, q);
        var y = FilterExpression.And(q, p);

        Assert.Equal(
            FilterExpression.ContentSignature(x),
            FilterExpression.ContentSignature(y));
        Assert.True(FilterExpression.StructuralComparer.Equals(x, y));
    }

    [Fact]
    public void ContentSignatureIsOrderIndependentForInValues()
    {
        var a = FilterExpression.In("id", [FilterValue.From(1L), FilterValue.From(2L), FilterValue.From(3L)]);
        var b = FilterExpression.In("id", [FilterValue.From(3L), FilterValue.From(1L), FilterValue.From(2L)]);

        Assert.Equal(
            FilterExpression.ContentSignature(a),
            FilterExpression.ContentSignature(b));
    }

    [Fact]
    public void CanonicalizeDropsRedundantAnyInAnd()
    {
        var p = FilterExpression.Compare("a", FilterOperator.Equal, FilterValue.From(1L));
        var withAny = new FilterExpression(FilterExpressionKind.And)
        {
            Children = [p, FilterExpression.Any],
        };

        FilterExpression canonical = FilterExpression.Canonicalize(withAny);
        Assert.Equal(FilterExpressionKind.Compare, canonical.Kind);
    }

    [Fact]
    public void DistinctFiltersHaveDistinctSignatures()
    {
        var a = FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU"));
        var b = FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("US"));

        Assert.NotEqual(FilterExpression.ContentSignature(a), FilterExpression.ContentSignature(b));
        Assert.False(FilterExpression.StructuralComparer.Equals(a, b));
    }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Generators.Tests;

public sealed class LargeIntegralNumberPrecisionRegressionTests
{
    private const long ExactDoubleInteger = 9_007_199_254_740_992L;
    private const long RoundedDecimalNeighbor = 9_007_199_254_740_993L;

    [Fact]
    public void RuntimeCompareUsesExactIntegralDoubleValue()
    {
        FilterValue expected = FilterValue.From((double)ExactDoubleInteger);

        Assert.True(FilterValues.Compare(ExactDoubleInteger, expected, FilterOperator.Equal));
        Assert.False(FilterValues.Compare(RoundedDecimalNeighbor, expected, FilterOperator.Equal));
    }

    [Fact]
    public void CompiledFiltersUseExactIntegralDoubleValue()
    {
        FilterValue expected = FilterValue.From((double)ExactDoubleInteger);

        AssertMatches(
            FilterExpression.Compare(nameof(LargeNumberSubject.Id), FilterOperator.Equal, expected));
        AssertMatches(
            FilterExpression.In(nameof(LargeNumberSubject.Id), [expected]));
        AssertMatches(
            FilterExpression.Contains(nameof(LargeNumberSubject.Ids), expected));
    }

    [Fact]
    public void EqualityIndexUsesExactIntegralDoubleValue()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(LargeNumberSubject));
        index.Add(
            "sub",
            FilterExpression.Compare(
                nameof(LargeNumberSubject.Id),
                FilterOperator.Equal,
                FilterValue.From((double)ExactDoubleInteger)));

        Assert.Equal(["sub"], index.SnapshotMatches(new LargeNumberSubject(ExactDoubleInteger, [])));
        Assert.Empty(index.SnapshotMatches(new LargeNumberSubject(RoundedDecimalNeighbor, [])));
    }

    [Fact]
    public void ArrayContainsUsesExactIntegralDoubleValue()
    {
        double expected = ExactDoubleInteger;

        Assert.True(FilterArrayContains.ContainsInt64([ExactDoubleInteger], expected));
        Assert.False(FilterArrayContains.ContainsInt64([RoundedDecimalNeighbor], expected));
    }

    private static void AssertMatches(FilterExpression filter)
    {
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(LargeNumberSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new LargeNumberSubject(ExactDoubleInteger, [ExactDoubleInteger])));
        Assert.False(kernel.Matches(new LargeNumberSubject(
            RoundedDecimalNeighbor,
            [RoundedDecimalNeighbor])));
    }

    private sealed record LargeNumberSubject(long Id, long[] Ids) : IFilterSubject;
}

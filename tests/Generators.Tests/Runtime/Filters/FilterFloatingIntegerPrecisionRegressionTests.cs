using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;

namespace SiftQL.Generators.Tests;

public sealed class FilterFloatingIntegerPrecisionRegressionTests
{
    private const long RoundedInteger = 9_007_199_254_740_992L;
    private const long NeighborInteger = 9_007_199_254_740_993L;

    [Fact]
    public void LargeIntegerLiteralDoesNotRoundToDoubleMatch()
    {
        FilterValue neighbor = FilterValue.From(NeighborInteger);
        var subject = new DoublePrecisionSubject((double)RoundedInteger, [(double)RoundedInteger]);

        Assert.False(FilterValues.Compare(subject.Score, neighbor, FilterOperator.Equal));
        Assert.True(FilterValues.Compare(subject.Score, neighbor, FilterOperator.LessThan));
        AssertFilter(
            FilterExpression.Compare(
                nameof(DoublePrecisionSubject.Score),
                FilterOperator.Equal,
                neighbor),
            new FilterCase(subject, false));
        AssertFilter(
            FilterExpression.Compare(
                nameof(DoublePrecisionSubject.Score),
                FilterOperator.LessThan,
                neighbor),
            new FilterCase(subject, true));
        AssertFilter(
            FilterExpression.In(nameof(DoublePrecisionSubject.Score), [neighbor]),
            new FilterCase(subject, false));
        AssertFilter(
            FilterExpression.Contains(nameof(DoublePrecisionSubject.Scores), neighbor),
            new FilterCase(subject, false));
    }

    private static void AssertFilter(FilterExpression filter, params FilterCase[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(DoublePrecisionSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(DoublePrecisionSubject),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

    private sealed record DoublePrecisionSubject(double Score, double[] Scores) : IFilterSubject;

    private sealed record FilterCase(DoublePrecisionSubject Subject, bool Expected);
}

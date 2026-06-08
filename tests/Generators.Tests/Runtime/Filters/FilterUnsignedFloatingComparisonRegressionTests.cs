using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;

namespace SiftQL.Generators.Tests;

public sealed class FilterUnsignedFloatingComparisonRegressionTests
{
    [Fact]
    public void ULongMaxLiteralDoesNotRoundToDoubleMatch()
    {
        FilterValue expected = FilterValue.From(ulong.MaxValue);
        var subject = new FloatingSubject((double)ulong.MaxValue);

        Assert.False(FilterValues.Compare(subject.Score, expected, FilterOperator.Equal));
        Assert.True(FilterValues.Compare(subject.Score, expected, FilterOperator.GreaterThan));
        AssertFilter(
            FilterExpression.Compare(
                nameof(FloatingSubject.Score),
                FilterOperator.Equal,
                expected),
            subject,
            expectedMatch: false);
        AssertFilter(
            FilterExpression.Compare(
                nameof(FloatingSubject.Score),
                FilterOperator.GreaterThan,
                expected),
            subject,
            expectedMatch: true);
    }

    private static void AssertFilter(
        FilterExpression filter,
        FloatingSubject subject,
        bool expectedMatch)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(FloatingSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(FloatingSubject),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.Equal(expectedMatch, immediate.Matches(subject));
        Assert.Equal(expectedMatch, tiered.Matches(subject));
    }

    private sealed record FloatingSubject(double Score) : IFilterSubject;
}

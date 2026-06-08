using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterNumericUnderflowRegressionTests
{
    [Fact]
    public void NonZeroDoubleUnderflowDoesNotEqualExactZero()
    {
        FilterValue underflow = FilterValue.From(double.Epsilon);

        Assert.False(FilterValues.Compare(0, underflow, FilterOperator.Equal));
        Assert.True(FilterValues.Compare(0, underflow, FilterOperator.LessThan));
        Assert.False(FilterValues.In(0, [underflow]));
        Assert.False(FilterValues.Contains(new[] { 0 }, underflow));
    }

    [Fact]
    public void NumericArrayHelpersRejectNonZeroDoubleUnderflowAsZero()
    {
        Assert.False(FilterArrayContains.ContainsInt32([0], double.Epsilon));
        Assert.False(FilterArrayContains.ContainsDecimal([0m], double.Epsilon));
    }

    [Fact]
    public void CompiledExactNumericFiltersRejectNonZeroDoubleUnderflowAsZero()
    {
        FilterValue underflow = FilterValue.From(double.Epsilon);
        var subject = new ExactZeroSubject(0, [0], 0m, [0m]);

        AssertFilter(
            FilterExpression.Compare(nameof(ExactZeroSubject.Count), FilterOperator.Equal, underflow),
            subject);
        AssertFilter(FilterExpression.In(nameof(ExactZeroSubject.Count), [underflow]), subject);
        AssertFilter(FilterExpression.Contains(nameof(ExactZeroSubject.Counts), underflow), subject);
        AssertFilter(FilterExpression.Contains(nameof(ExactZeroSubject.Amounts), underflow), subject);
    }

    private static void AssertFilter(FilterExpression filter, ExactZeroSubject subject)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(ExactZeroSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(ExactZeroSubject),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.False(immediate.Matches(subject));
        Assert.False(tiered.Matches(subject));
    }

    private sealed record ExactZeroSubject(
        int Count,
        int[] Counts,
        decimal Amount,
        decimal[] Amounts) : IFilterSubject;
}

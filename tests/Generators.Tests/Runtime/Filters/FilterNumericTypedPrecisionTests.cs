using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterNumericTypedPrecisionTests
{
    private const long RoundedInteger = 9_007_199_254_740_992L;
    private const long NeighborInteger = 9_007_199_254_740_993L;

    [Fact]
    public void TypedDecimalEqualityUsesExactConstantSemantics()
    {
        QueryKernel<NumericSubject> kernel = QueryKernel.For<NumericSubject>()
            .Where(static subject => subject.Amount == 9_007_199_254_740_993m);

        AssertFilter(
            kernel.Filter,
            new FilterCase<NumericSubject>(
                Subject(amount: RoundedInteger),
                false));
    }

    [Fact]
    public void TypedDecimalInUsesExactConstantSemantics()
    {
        decimal[] accepted = [9_007_199_254_740_993m];
        QueryKernel<NumericSubject> kernel = QueryKernel.For<NumericSubject>()
            .Where(subject => accepted.Contains(subject.Amount));

        AssertFilter(
            kernel.Filter,
            new FilterCase<NumericSubject>(
                Subject(amount: RoundedInteger),
                false));
    }

    [Fact]
    public void ParameterizedNullableNumericOrderingRejectsNullActuals()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(NumericSubject.OptionalCount),
            FilterOperator.LessThan,
            FilterValue.From(10L) with { ParameterKey = "p0" });

        AssertFilter(
            filter,
            new FilterCase<NumericSubject>(
                Subject(optionalCount: null),
                false));
    }

    [Theory]
    [InlineData(FilterOperator.GreaterThan)]
    [InlineData(FilterOperator.GreaterThanOrEqual)]
    [InlineData(FilterOperator.LessThan)]
    [InlineData(FilterOperator.LessThanOrEqual)]
    public void OrderedNumberComparisonsRejectNaN(FilterOperator op)
    {
        Assert.False(FilterValues.Compare(double.NaN, FilterValue.From(0D), op));
    }

    private static void AssertFilter<TSubject>(
        FilterExpression filter,
        params FilterCase<TSubject>[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase<TSubject> item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject!));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject!));
        }
    }

    private static NumericSubject Subject(decimal? amount = null, int? optionalCount = 1) =>
        new(amount ?? NeighborInteger, optionalCount);

    private sealed record NumericSubject(decimal Amount, int? OptionalCount) : IFilterSubject;
    private sealed record FilterCase<TSubject>(TSubject Subject, bool Expected);
}

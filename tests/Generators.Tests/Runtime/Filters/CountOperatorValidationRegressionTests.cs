using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CountOperatorValidationRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountRejectsStringOperatorsDuringCompilation(bool tiered)
    {
        FilterExpression filter = FilterExpression.Count(
            nameof(CountSubject.Tags),
            FilterOperator.StringContains,
            FilterValue.From(1L));

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(
                typeof(CountSubject),
                filter,
                tiered ? FilterCompilerOptions.Tiered : FilterCompilerOptions.Immediate));
    }

    [Fact]
    public void ParameterizedCountRejectsStringOperatorsDuringCompilation()
    {
        FilterExpression filter = FilterExpression.Count(
            nameof(CountSubject.Tags),
            FilterOperator.StringContains,
            FilterValue.From(1L) with { ParameterKey = "count" });

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(
                typeof(CountSubject),
                filter,
                FilterCompilerOptions.Immediate));
    }

    private sealed record CountSubject(string[] Tags) : IFilterSubject;
}

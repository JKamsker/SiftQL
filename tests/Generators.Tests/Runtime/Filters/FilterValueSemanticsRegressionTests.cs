using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;

namespace SiftQL.Generators.Tests;

public sealed class FilterValueSemanticsRegressionTests
{
    [Fact]
    public void ULongBackedEnumMatchesUnsignedLiteral()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(BigEnumSubject.Kind),
            FilterOperator.Equal,
            FilterValue.From(ulong.MaxValue));

        CompiledKernel kernel = FilterCompiler.Compile(typeof(BigEnumSubject), filter);

        Assert.True(kernel.Matches(new BigEnumSubject(BigEnum.Huge)));
    }

    [Fact]
    public void ContainsEnumerableReturnsAfterFirstMatch()
    {
        bool matched = FilterValues.Contains(MatchingThenThrow(), FilterValue.From(42L));

        Assert.True(matched);
    }

    private static IEnumerable<int> MatchingThenThrow()
    {
        yield return 42;
        throw new InvalidOperationException("enumerated after match");
    }

    private enum BigEnum : ulong
    {
        Huge = ulong.MaxValue,
    }

    private sealed record BigEnumSubject(BigEnum Kind) : IFilterSubject;
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionDepthLimitRegressionTests
{
    // Deep enough to overflow the stack if validation recurses unbounded.
    private const int HostileDepth = 100_000;

    [Fact]
    public void Compile_HostileDeepFilter_ThrowsCleanValidationError()
    {
        FilterExpression filter = DeepNotChain(HostileDepth);

        var error = Assert.Throws<FilterValidationException>(
            () => FilterCompiler.Compile(typeof(DepthSubject), filter, FilterCompilerOptions.Immediate));

        Assert.Contains("depth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexAdd_HostileDeepFilter_ThrowsCleanValidationError()
    {
        FilterExpression filter = DeepNotChain(HostileDepth);
        var index = new TypedFilterSubscriptionIndex<string, DepthSubject>();

        Assert.Throws<FilterValidationException>(() => index.Add("deep", filter));
    }

    [Fact]
    public void Compile_ReasonablyNestedFilter_StillWorks()
    {
        FilterExpression filter = DeepNotChain(8);

        var kernel = FilterCompiler.Compile(typeof(DepthSubject), filter, FilterCompilerOptions.Immediate);

        // 8 Not wrappers around Value == 1: even depth keeps the comparison.
        Assert.True(kernel.Matches(new DepthSubject(1)));
        Assert.False(kernel.Matches(new DepthSubject(2)));
    }

    private static FilterExpression DeepNotChain(int depth)
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(DepthSubject.Value),
            FilterOperator.Equal,
            FilterValue.From(1L));
        for (int i = 0; i < depth; i++)
            filter = FilterExpression.Not(filter);

        return filter;
    }

    private sealed record DepthSubject(long Value) : IFilterSubject;
}

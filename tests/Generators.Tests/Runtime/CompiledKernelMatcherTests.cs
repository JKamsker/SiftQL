using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CompiledKernelMatcherTests
{
    private sealed record ScalarSubject(
        int Count = 0,
        string? Name = null) : IFilterSubject;

    [Fact]
    public void KernelMatcher_AlwaysTrue_ReturnsTrue()
    {
        var matcher = CompiledKernel.Any.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject()));
    }

    [Fact]
    public void KernelMatcher_ImmediateKernel_MatchesAndRejects()
    {
        var filter = FilterExpression.Compare(nameof(ScalarSubject.Count), FilterOperator.Equal, FilterValue.From(7L));
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject(Count: 7)));
        Assert.False(matcher.Matches(new ScalarSubject(Count: 8)));
    }

    [Fact]
    public void KernelMatcher_MultipleCallsNonTiered_StableResults()
    {
        var filter = FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.Equal, FilterValue.From("ok"));
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(matcher.Matches(new ScalarSubject(Name: "ok")));
            Assert.False(matcher.Matches(new ScalarSubject(Name: "no")));
        }
    }

    [Fact]
    public void KernelMatcher_ObjectPredicateOnly_FallsBack()
    {
        var kernel = new CompiledKernel(static obj => obj is ScalarSubject s && s.Count == 99, isBroad: false);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject(Count: 99)));
        Assert.False(matcher.Matches(new ScalarSubject(Count: 1)));
    }
}

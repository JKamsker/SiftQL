using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Generators.Tests;

public sealed class FilterCacheBetweenFieldRegressionTests
{
    [Fact]
    public void BetweenCacheKeySeparatesFieldName()
    {
        FilterExpression score = FilterExpression.Between(
            nameof(BetweenSubject.Score),
            FilterValue.From(10L),
            FilterValue.From(20L));
        FilterExpression level = FilterExpression.Between(
            nameof(BetweenSubject.Level),
            FilterValue.From(10L),
            FilterValue.From(20L));

        _ = FilterCompiler.Compile(typeof(BetweenSubject), score, FilterCompilerOptions.Immediate);
        CompiledKernel levelKernel = FilterCompiler.Compile(
            typeof(BetweenSubject),
            level,
            FilterCompilerOptions.Immediate);

        Assert.True(levelKernel.Matches(new BetweenSubject(Score: 0, Level: 15)));
    }

    private sealed record BetweenSubject(int Score, int Level) : IFilterSubject;
}

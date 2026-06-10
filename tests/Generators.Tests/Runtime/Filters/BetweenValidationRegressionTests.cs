using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class BetweenValidationRegressionTests
{
    [Fact]
    public void ValidatorRejectsBetweenOnUnorderedStringField()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(BetweenSubject.Name),
            FilterValue.From("a"),
            FilterValue.From("z"));

        FilterValidationResult result = FilterValidator.Validate(typeof(BetweenSubject), filter);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParameterizedBetweenRejectsBoundsIncompatibleWithNumericField()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(BetweenSubject.Score),
            FilterValue.From("low") with { ParameterKey = "min" },
            FilterValue.From("high") with { ParameterKey = "max" });

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(
                typeof(BetweenSubject),
                filter,
                FilterCompilerOptions.Immediate));
    }

    private sealed record BetweenSubject(string Name, int Score) : IFilterSubject;
}

using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterValueJsonNonFiniteRegressionTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FilterValue_NonFiniteNumber_RoundTripsThroughJson(double number)
    {
        FilterValue value = FilterValue.From(number);

        string json = JsonSerializer.Serialize(value);
        FilterValue restored = JsonSerializer.Deserialize<FilterValue>(json)!;

        Assert.Equal(FilterValueKind.Number, restored.Kind);
        Assert.Equal(number, restored.Number);
    }

    [Fact]
    public void FilterExpression_NonFiniteNumber_RoundTripsAndMatchesLikeOriginal()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(Reading.Value),
            FilterOperator.GreaterThan,
            FilterValue.From(double.NegativeInfinity));

        string json = JsonSerializer.Serialize(filter);
        FilterExpression restored = JsonSerializer.Deserialize<FilterExpression>(json)!;
        var kernel = FilterCompiler.Compile(typeof(Reading), restored, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new Reading(1.0)));
        Assert.False(kernel.Matches(new Reading(double.NegativeInfinity)));
    }

    private sealed record Reading(double Value) : IFilterSubject;
}

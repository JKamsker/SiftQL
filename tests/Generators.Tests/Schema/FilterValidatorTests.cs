using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterValidatorTests
{
    [Fact]
    public void ValidFilterReturnsValid()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare(nameof(Ev.Name), FilterOperator.Equal, FilterValue.From("x")),
            FilterExpression.Compare(nameof(Ev.Score), FilterOperator.GreaterThan, FilterValue.From(1L)));

        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), filter);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AggregatesAllUnknownFieldErrors()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("Nope", FilterOperator.Equal, FilterValue.From(1L)),
            FilterExpression.Compare("AlsoNope", FilterOperator.Equal, FilterValue.From(2L)));

        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), filter);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.StartsWith("$", error.Path));
    }

    [Fact]
    public void ReportsTypeIncompatibleOperator()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(Ev.Name),
            FilterOperator.GreaterThan,
            FilterValue.From("x"));

        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), filter);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ReportsDepthLimitBreach()
    {
        FilterExpression filter = FilterExpression.Compare(nameof(Ev.Score), FilterOperator.Equal, FilterValue.From(1L));
        for (int i = 0; i < 5; i++)
            filter = FilterExpression.Not(filter);

        FilterValidationResult result = FilterValidator.Validate(
            typeof(Ev),
            filter,
            new FilterLimits { MaxDepth = 2 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("depth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JsonOverloadReportsParseErrorInsteadOfThrowing()
    {
        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), "{not valid json");

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void JsonOverloadEnforcesByteLimit()
    {
        string json = FilterDocument.Serialize(
            FilterExpression.Compare(nameof(Ev.Name), FilterOperator.Equal, FilterValue.From("x")));

        FilterValidationResult result = FilterValidator.Validate(
            typeof(Ev),
            json,
            new FilterLimits { MaxBytes = 4 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("byte", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Ev(string Name, int Score) : IFilterSubject;
}

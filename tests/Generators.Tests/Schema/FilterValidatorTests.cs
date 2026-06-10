using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
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
    public void ReportsNullLiteralForOrderedComparison()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(Ev.Score),
            FilterOperator.GreaterThan,
            FilterValue.Null);

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

    [Fact]
    public void ReportsReversedBetweenBounds()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Ev.Score),
            FilterValue.From(10L),
            FilterValue.From(1L));

        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), filter);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("lower", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AcceptsOrderedBetweenBounds()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Ev.Score),
            FilterValue.From(1L),
            FilterValue.From(10L));

        FilterValidationResult result = FilterValidator.Validate(typeof(Ev), filter);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReportsCountWithNonComparisonOperator()
    {
        FilterExpression filter = FilterExpression.Count(
            nameof(Bag.Tags),
            FilterOperator.StringContains,
            FilterValue.From(1L));

        FilterValidationResult result = FilterValidator.Validate(typeof(Bag), filter);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Message.Contains("comparison operator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatesElemMatchChildAgainstElementSchema()
    {
        FilterSchema.RegisterValueObject(typeof(Loot));

        FilterExpression filter = FilterExpression.ElemMatch(
            nameof(Chest.Items),
            FilterExpression.Compare(nameof(Loot.Tag), FilterOperator.Equal, FilterValue.From("rare")));

        FilterValidationResult result = FilterValidator.Validate(typeof(Chest), filter);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ReportsElemMatchChildFieldMissingOnElement()
    {
        FilterSchema.RegisterValueObject(typeof(Loot));

        FilterExpression filter = FilterExpression.ElemMatch(
            nameof(Chest.Items),
            FilterExpression.Compare("Missing", FilterOperator.Equal, FilterValue.From("x")));

        FilterValidationResult result = FilterValidator.Validate(typeof(Chest), filter);

        Assert.False(result.IsValid);
    }

    private sealed record Ev(string Name, int Score) : IFilterSubject;
    private sealed record Bag(string[] Tags) : IFilterSubject;
    private sealed record Chest(Loot[] Items) : IFilterSubject;
    private sealed record Loot(string Tag);
}

using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelBetweenRangeTests
{
    [Fact]
    public void BetweenFactoryMatchesInclusiveRange()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Reading.Score),
            FilterValue.From(10L),
            FilterValue.From(20L));

        Assert.Equal(FilterExpressionKind.Between, filter.Kind);
        AssertFilter(filter,
            new Case(new Reading(10, default), true),
            new Case(new Reading(15, default), true),
            new Case(new Reading(20, default), true),
            new Case(new Reading(9, default), false),
            new Case(new Reading(21, default), false));
    }

    [Fact]
    public void BetweenParsesFromDsl()
    {
        FilterExpression filter = FilterQuery.Parse("Score between [10, 20]");

        Assert.Equal(FilterExpressionKind.Between, filter.Kind);
        AssertFilter(filter,
            new Case(new Reading(15, default), true),
            new Case(new Reading(25, default), false));
    }

    [Fact]
    public void BetweenRoundTripsJsonAndFormat()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Reading.Score),
            FilterValue.From(1L),
            FilterValue.From(5L));

        FilterExpression fromJson = JsonSerializer.Deserialize<FilterExpression>(JsonSerializer.Serialize(filter))!;
        FilterExpression fromText = FilterQuery.Parse(FilterQuery.Format(filter));

        Assert.Equal(FilterExpression.ContentSignature(filter), FilterExpression.ContentSignature(fromJson));
        Assert.Equal(FilterExpression.ContentSignature(filter), FilterExpression.ContentSignature(fromText));
    }

    [Fact]
    public void BetweenMatchesTemporalRange()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Reading.At),
            FilterValue.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            FilterValue.From(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));

        AssertFilter(filter,
            new Case(new Reading(0, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), true),
            new Case(new Reading(0, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)), false));
    }

    [Fact]
    public void BetweenOnNonScalarRejected()
    {
        FilterExpression filter = FilterExpression.Between(
            nameof(Reading.Tags),
            FilterValue.From(1L),
            FilterValue.From(2L));

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(Reading), filter, FilterCompilerOptions.Immediate));
    }

    private static void AssertFilter(FilterExpression filter, params Case[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(typeof(Reading), filter, FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(typeof(Reading), filter, FilterCompilerOptions.Tiered);
        foreach (Case item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

    private sealed record Reading(int Score, DateTimeOffset At, int[]? Tags = null) : IFilterSubject;
    private sealed record Case(Reading Subject, bool Expected);
}

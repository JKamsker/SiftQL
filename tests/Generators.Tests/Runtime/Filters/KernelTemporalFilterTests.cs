using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelTemporalFilterTests
{
    [Fact]
    public void FromObjectSupportsTemporalTypes()
    {
        Assert.Equal(FilterValueKind.Timestamp, FilterValue.FromObject(DateTimeOffset.UnixEpoch).Kind);
        Assert.Equal(FilterValueKind.Timestamp, FilterValue.FromObject(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Kind);
        Assert.Equal(FilterValueKind.Timestamp, FilterValue.FromObject(new DateOnly(2026, 1, 1)).Kind);
    }

    [Fact]
    public void TemporalFieldIsRegisteredInSchema()
    {
        Assert.Contains(
            nameof(Telemetry.OccurredAt),
            FilterSchema.For(typeof(Telemetry)).FieldNames);
    }

    [Fact]
    public void WhereTimestampComparisonMatchesByInstant()
    {
        var cutoff = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        QueryKernel<Telemetry> query = QueryKernel.For<Telemetry>()
            .Where(e => e.OccurredAt >= cutoff);

        Assert.Equal(FilterValueKind.Timestamp, query.Filter.Value!.Kind);

        var kernel = FilterCompiler.Compile(typeof(Telemetry), query.Filter, FilterCompilerOptions.Immediate);
        var tiered = FilterCompiler.Compile(typeof(Telemetry), query.Filter, FilterCompilerOptions.Tiered);

        var after = new Telemetry(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), default, 0);
        var before = new Telemetry(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), default, 0);

        Assert.True(kernel.Matches(after));
        Assert.False(kernel.Matches(before));
        Assert.True(tiered.Matches(after));
        Assert.False(tiered.Matches(before));
    }

    [Fact]
    public void EqualityMatchesSameInstantAcrossOffsets()
    {
        var filter = FilterExpression.Compare(
            nameof(Telemetry.OccurredAt),
            FilterOperator.Equal,
            FilterValue.From(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));

        var kernel = FilterCompiler.Compile(typeof(Telemetry), filter, FilterCompilerOptions.Immediate);

        // Same instant expressed in a +02:00 offset.
        var equivalent = new Telemetry(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.FromHours(2)), default, 0);
        var different = new Telemetry(new DateTimeOffset(2026, 6, 1, 12, 0, 1, TimeSpan.Zero), default, 0);

        Assert.True(kernel.Matches(equivalent));
        Assert.False(kernel.Matches(different));
    }

    [Fact]
    public void TimestampValueRoundTripsJson()
    {
        var filter = FilterExpression.Compare(
            "ts",
            FilterOperator.Equal,
            FilterValue.From(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));

        string json = JsonSerializer.Serialize(filter);
        FilterExpression? restored = JsonSerializer.Deserialize<FilterExpression>(json);

        Assert.Equal(FilterValueKind.Timestamp, restored!.Value!.Kind);
        Assert.Equal(filter.Value!.Timestamp, restored.Value.Timestamp);
    }

    [Fact]
    public void FilterValuesCompareTemporalDirectly()
    {
        var expected = FilterValue.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(FilterValues.Compare(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), expected, FilterOperator.Equal));
        Assert.True(FilterValues.Compare(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), expected, FilterOperator.GreaterThan));
        Assert.False(FilterValues.Compare("not a date", expected, FilterOperator.Equal));
    }

    private sealed record Telemetry(DateTimeOffset OccurredAt, DateTime Recorded, int Value) : IFilterSubject;
}

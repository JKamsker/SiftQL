using System.Linq;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexRangeTests
{
    [Fact]
    public void ThresholdSubscriptionIsRangeIndexed()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("hot", FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.GreaterThan, FilterValue.From(80L)));

        FilterSubscriptionIndexStatistics stats = index.GetStatistics();
        Assert.Equal(0, stats.UnindexedCount);
        Assert.Equal(1, stats.RangeIndexedCount);

        Assert.Equal(new[] { "hot" }, index.SnapshotMatches(new Sensor(90, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(80, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(70, default, 0)));
    }

    [Fact]
    public void BetweenSubscriptionIsRangeIndexed()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("warm", FilterExpression.Between(nameof(Sensor.Temperature), FilterValue.From(60L), FilterValue.From(80L)));

        Assert.Equal(1, index.GetStatistics().RangeIndexedCount);
        Assert.Equal(new[] { "warm" }, index.SnapshotMatches(new Sensor(70, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(50, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(90, default, 0)));
    }

    [Fact]
    public void AndRangeMergesBounds()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("band", FilterExpression.And(
            FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.GreaterThanOrEqual, FilterValue.From(60L)),
            FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.LessThanOrEqual, FilterValue.From(80L))));

        Assert.Equal(1, index.GetStatistics().RangeIndexedCount);
        Assert.Equal(new[] { "band" }, index.SnapshotMatches(new Sensor(60, default, 0)));
        Assert.Equal(new[] { "band" }, index.SnapshotMatches(new Sensor(80, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(59, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(81, default, 0)));
    }

    [Fact]
    public void ManyThresholdsReturnCorrectSubset()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        for (int i = 0; i < 100; i++)
            index.Add($"gt{i}", FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.GreaterThan, FilterValue.From((long)i)));

        Assert.Equal(100, index.GetStatistics().RangeIndexedCount);

        string[] matches = index.SnapshotMatches(new Sensor(50, default, 0));
        // Temperature 50 > i for i in 0..49.
        Assert.Equal(50, matches.Length);
        Assert.Contains("gt0", matches);
        Assert.Contains("gt49", matches);
        Assert.DoesNotContain("gt50", matches);
    }

    [Fact]
    public void TemporalRangeIsIndexed()
    {
        var cutoff = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("recent", FilterExpression.Compare(nameof(Sensor.At), FilterOperator.GreaterThanOrEqual, FilterValue.From(cutoff)));

        Assert.Equal(1, index.GetStatistics().RangeIndexedCount);
        Assert.Equal(new[] { "recent" }, index.SnapshotMatches(new Sensor(0, new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(0, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), 0)));
    }

    [Fact]
    public void DoubleFieldThresholdStaysUnindexedButMatches()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("p", FilterExpression.Compare(nameof(Sensor.Pressure), FilterOperator.GreaterThan, FilterValue.From(1.5)));

        FilterSubscriptionIndexStatistics stats = index.GetStatistics();
        Assert.Equal(0, stats.RangeIndexedCount);
        Assert.Equal(1, stats.UnindexedCount);

        Assert.Equal(new[] { "p" }, index.SnapshotMatches(new Sensor(0, default, 2.0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(0, default, 1.0)));
    }

    [Fact]
    public void RangeRemovalWorks()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(Sensor));
        index.Add("hot", FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.GreaterThan, FilterValue.From(80L)));
        index.Remove("hot");

        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.GetStatistics().RangeIndexedCount);
        Assert.Empty(index.SnapshotMatches(new Sensor(90, default, 0)));
    }

    [Fact]
    public void TypedIndexRangeIsAccelerated()
    {
        var index = new TypedFilterSubscriptionIndex<string, Sensor>();
        index.Add("hot", FilterExpression.Compare(nameof(Sensor.Temperature), FilterOperator.GreaterThan, FilterValue.From(80L)));

        Assert.Equal(1, index.GetStatistics().RangeIndexedCount);
        Assert.Equal(new[] { "hot" }, index.SnapshotMatches(new Sensor(90, default, 0)));
        Assert.Empty(index.SnapshotMatches(new Sensor(70, default, 0)));
    }

    private sealed record Sensor(int Temperature, DateTimeOffset At, double Pressure) : IFilterSubject;
}

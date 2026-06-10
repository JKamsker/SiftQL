using SiftQL;
using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexStatisticsTests
{
    [Fact]
    public void StatisticsReportIndexedAndUnindexedPlacement()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(RoutedEvent));
        index.Add("in", FilterExpression.In(
            nameof(RoutedEvent.CharacterId),
            [FilterValue.From(1L), FilterValue.From(2L), FilterValue.From(3L)]));
        index.Add("scan", FilterExpression.StringContains(
            nameof(RoutedEvent.Type),
            FilterValue.From("x")));

        FilterSubscriptionIndexStatistics stats = index.GetStatistics();

        Assert.Equal(2, stats.Count);
        Assert.Equal(1, stats.IndexedCount);
        Assert.Equal(1, stats.UnindexedCount);
        Assert.Equal(3, stats.BucketsByField[nameof(RoutedEvent.CharacterId)]);
    }

    [Fact]
    public void StatisticsTrackRemoval()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(RoutedEvent));
        index.Add("in", FilterExpression.In(
            nameof(RoutedEvent.CharacterId),
            [FilterValue.From(1L), FilterValue.From(2L)]));
        index.Remove("in");

        FilterSubscriptionIndexStatistics stats = index.GetStatistics();

        Assert.Equal(0, stats.Count);
        Assert.Equal(0, stats.IndexedCount);
        Assert.Empty(stats.BucketsByField);
    }

    [Fact]
    public void TypedIndexStatisticsReportPlacement()
    {
        var index = new TypedFilterSubscriptionIndex<string, RoutedEvent>();
        index.Add("eq", FilterExpression.Compare(
            nameof(RoutedEvent.Type),
            FilterOperator.Equal,
            FilterValue.From("A")));

        FilterSubscriptionIndexStatistics stats = index.GetStatistics();

        Assert.Equal(1, stats.Count);
        Assert.Equal(1, stats.IndexedCount);
        Assert.Equal(0, stats.UnindexedCount);
        Assert.Equal(1, stats.BucketsByField[nameof(RoutedEvent.Type)]);
    }

    private sealed record RoutedEvent(long CharacterId, string Type) : IFilterSubject;
}

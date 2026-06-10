using System.Collections.Generic;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexExtractorMultiKeyTests
{
    [Fact]
    public void ExtractKeysReturnsOneKeyPerInValue()
    {
        var filter = FilterExpression.In(
            nameof(RoutedEvent.CharacterId),
            [FilterValue.From(1L), FilterValue.From(2L), FilterValue.From(3L)]);

        IReadOnlyList<FilterIndexKey> keys = FilterIndexExtractor.ExtractKeys(typeof(RoutedEvent), filter);

        Assert.Equal(3, keys.Count);
        Assert.All(keys, key => Assert.Equal(nameof(RoutedEvent.CharacterId), key.Field));
    }

    [Fact]
    public void ExtractKeysReturnsKeyPerOrBranchOfEqualities()
    {
        var filter = FilterExpression.Or(
            FilterExpression.Compare(nameof(RoutedEvent.Type), FilterOperator.Equal, FilterValue.From("A")),
            FilterExpression.Compare(nameof(RoutedEvent.Type), FilterOperator.Equal, FilterValue.From("B")));

        IReadOnlyList<FilterIndexKey> keys = FilterIndexExtractor.ExtractKeys(typeof(RoutedEvent), filter);

        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void ExtractKeysReturnsEmptyWhenAnyOrBranchIsNotIndexable()
    {
        var filter = FilterExpression.Or(
            FilterExpression.Compare(nameof(RoutedEvent.Type), FilterOperator.Equal, FilterValue.From("A")),
            FilterExpression.Compare(nameof(RoutedEvent.Score), FilterOperator.GreaterThan, FilterValue.From(10L)));

        IReadOnlyList<FilterIndexKey> keys = FilterIndexExtractor.ExtractKeys(typeof(RoutedEvent), filter);

        Assert.Empty(keys);
    }

    [Fact]
    public void ExtractKeysIndexesInBranchOfAnd()
    {
        var filter = FilterExpression.And(
            FilterExpression.In(nameof(RoutedEvent.CharacterId), [FilterValue.From(1L), FilterValue.From(2L)]),
            FilterExpression.Compare(nameof(RoutedEvent.Score), FilterOperator.GreaterThan, FilterValue.From(10L)));

        IReadOnlyList<FilterIndexKey> keys = FilterIndexExtractor.ExtractKeys(typeof(RoutedEvent), filter);

        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.Equal(nameof(RoutedEvent.CharacterId), key.Field));
    }

    [Fact]
    public void InSubscriptionMatchesAllValuesThroughIndex()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(RoutedEvent));
        index.Add("sub", FilterExpression.In(
            nameof(RoutedEvent.CharacterId),
            [FilterValue.From(1L), FilterValue.From(2L), FilterValue.From(3L)]));

        Assert.Equal(new[] { "sub" }, index.SnapshotMatches(new RoutedEvent(1, "X", 0)));
        Assert.Equal(new[] { "sub" }, index.SnapshotMatches(new RoutedEvent(3, "X", 0)));
        Assert.Empty(index.SnapshotMatches(new RoutedEvent(9, "X", 0)));
    }

    [Fact]
    public void OrSubscriptionMatchesEachBranchThroughIndex()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(RoutedEvent));
        index.Add("sub", FilterExpression.Or(
            FilterExpression.Compare(nameof(RoutedEvent.Type), FilterOperator.Equal, FilterValue.From("A")),
            FilterExpression.Compare(nameof(RoutedEvent.Type), FilterOperator.Equal, FilterValue.From("B"))));

        Assert.Equal(new[] { "sub" }, index.SnapshotMatches(new RoutedEvent(0, "A", 0)));
        Assert.Equal(new[] { "sub" }, index.SnapshotMatches(new RoutedEvent(0, "B", 0)));
        Assert.Empty(index.SnapshotMatches(new RoutedEvent(0, "C", 0)));
    }

    [Fact]
    public void InSubscriptionRemovedFromAllBuckets()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(RoutedEvent));
        index.Add("sub", FilterExpression.In(
            nameof(RoutedEvent.CharacterId),
            [FilterValue.From(1L), FilterValue.From(2L)]));
        index.Remove("sub");

        Assert.Equal(0, index.Count);
        Assert.Empty(index.SnapshotMatches(new RoutedEvent(1, "X", 0)));
        Assert.Empty(index.SnapshotMatches(new RoutedEvent(2, "X", 0)));
    }

    private sealed record RoutedEvent(long CharacterId, string Type, long Score) : IFilterSubject;
}

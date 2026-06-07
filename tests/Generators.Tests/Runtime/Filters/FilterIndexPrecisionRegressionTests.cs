using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexPrecisionRegressionTests
{
    [Fact]
    public void EnumEqualityMatchesThroughIndex()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(IndexedReviewEvent));
        index.Add(
            "enum",
            FilterExpression.Compare(
                nameof(IndexedReviewEvent.Kind),
                FilterOperator.Equal,
                FilterValue.From(nameof(IndexedReviewKind.Target))));

        Assert.Equal(["enum"], index.SnapshotCandidates(Event(IndexedReviewKind.Target, objectId: 1)));
        Assert.Empty(index.SnapshotCandidates(Event(IndexedReviewKind.Other, objectId: 1)));
    }

    [Fact]
    public void LargeLongEqualityDoesNotMatchRoundedNeighborThroughIndex()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(IndexedReviewEvent));
        index.Add(
            "long",
            FilterExpression.Compare(
                nameof(IndexedReviewEvent.ObjectId),
                FilterOperator.Equal,
                FilterValue.From(9_007_199_254_740_993L)));

        Assert.Empty(index.SnapshotCandidates(Event(IndexedReviewKind.Target, 9_007_199_254_740_992L)));
        Assert.Equal(["long"], index.SnapshotCandidates(Event(IndexedReviewKind.Target, 9_007_199_254_740_993L)));
    }

    [Fact]
    public void ExactNumberEqualityMatchesSignedIntegralThroughIndex()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(IndexedReviewEvent));
        index.Add(
            "number",
            FilterExpression.Compare(
                nameof(IndexedReviewEvent.ObjectId),
                FilterOperator.Equal,
                FilterValue.From(42D)));

        Assert.Equal(["number"], index.SnapshotCandidates(Event(IndexedReviewKind.Target, 42L)));
        Assert.Empty(index.SnapshotCandidates(Event(IndexedReviewKind.Target, 43L)));
    }

    private static IndexedReviewEvent Event(IndexedReviewKind kind, long objectId) =>
        new(Guid.NewGuid(), objectId, kind);

    private enum IndexedReviewKind
    {
        Other = 1,
        Target = 2,
    }

    private sealed record IndexedReviewEvent(
        Guid EventId,
        long ObjectId,
        IndexedReviewKind Kind) : IFilterSubject;
}

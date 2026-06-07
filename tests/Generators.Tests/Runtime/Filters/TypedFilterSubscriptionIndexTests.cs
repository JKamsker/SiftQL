using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class TypedFilterSubscriptionIndexTests
{
    [Fact]
    public void TypedIndexTracksCountAndRemovesIndexedSubscriptions()
    {
        var index = new TypedFilterSubscriptionIndex<string, IndexedSubject>();
        index.Add("a", IdEquals(10));
        index.Add("b", IdEquals(20));

        Assert.Equal(2, index.Count);
        Assert.Equal(["a"], index.SnapshotCandidates(new IndexedSubject(10, "north")));
        Assert.Equal(["b"], index.SnapshotCandidates(new IndexedSubject(20, "south")));

        index.Remove("a");

        Assert.Equal(1, index.Count);
        Assert.Empty(index.SnapshotCandidates(new IndexedSubject(10, "north")));
        Assert.Equal(["b"], index.SnapshotCandidates(new IndexedSubject(20, "south")));
    }

    [Fact]
    public void TypedIndexIncludesUnindexedSubscriptionsAndStopsVisitorWhenRequested()
    {
        var index = new TypedFilterSubscriptionIndex<string, IndexedSubject>();
        index.Add("all", FilterExpression.Any);
        index.Add("region", FilterExpression.Compare(
            nameof(IndexedSubject.Region),
            FilterOperator.Equal,
            FilterValue.From("north")));
        List<string> visited = [];

        bool Visitor(string subscription, ref List<string> state)
        {
            state.Add(subscription);
            return false;
        }

        index.ForEachCandidate(new IndexedSubject(1, "north"), ref visited, Visitor);

        Assert.Equal(["all"], visited);
        Assert.Equal(
            ["all", "region"],
            index.SnapshotCandidates(new IndexedSubject(1, "north")).Order().ToArray());
    }

    [Fact]
    public void UntypedIndexIgnoresSubjectsOfTheWrongRuntimeType()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(IndexedSubject));
        index.Add("id", IdEquals(10));

        Assert.Empty(index.SnapshotCandidates(new object()));
        Assert.Equal(["id"], index.SnapshotCandidates(new IndexedSubject(10, "north")));
    }

    private static FilterExpression IdEquals(int id) =>
        FilterExpression.Compare(
            nameof(IndexedSubject.Id),
            FilterOperator.Equal,
            FilterValue.From(id));

    private sealed record IndexedSubject(int Id, string Region) : IFilterSubject;
}

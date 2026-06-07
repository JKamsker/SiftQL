using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexMatchingTests
{
    [Fact]
    public void FSubIdx_SnapshotMatches_FiltersUnindexedCandidates()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("high", FilterExpression.Compare(
            nameof(SubjectA.Score),
            FilterOperator.GreaterThan,
            FilterValue.From(80.0)));

        Assert.Equal(["high"], index.SnapshotCandidates(new SubjectA(Score: 10.0)));
        Assert.Empty(index.SnapshotMatches(new SubjectA(Score: 10.0)));
        Assert.Equal(["high"], index.SnapshotMatches(new SubjectA(Score: 90.0)));
    }

    [Fact]
    public void TypedIdx_SnapshotMatches_FiltersPartiallyIndexedCandidates()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("zone-pressure", FilterExpression.And(
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("A")),
            FilterExpression.Compare(nameof(SubjectA.Score), FilterOperator.GreaterThan, FilterValue.From(80.0))));

        Assert.Equal(["zone-pressure"], index.SnapshotCandidates(new SubjectA(Region: "A", Score: 10.0)));
        Assert.Empty(index.SnapshotMatches(new SubjectA(Region: "A", Score: 10.0)));
        Assert.Equal(["zone-pressure"], index.SnapshotMatches(new SubjectA(Region: "A", Score: 90.0)));
    }

    [Fact]
    public void TypedIdx_SnapshotMatches_FiltersUnindexedOrCandidates()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("region-or-score", FilterExpression.Or(
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("A")),
            FilterExpression.Compare(nameof(SubjectA.Score), FilterOperator.GreaterThan, FilterValue.From(80.0))));

        Assert.Equal(["region-or-score"], index.SnapshotCandidates(new SubjectA(Region: "B", Score: 10.0)));
        Assert.Empty(index.SnapshotMatches(new SubjectA(Region: "B", Score: 10.0)));
        Assert.Equal(["region-or-score"], index.SnapshotMatches(new SubjectA(Region: "A", Score: 10.0)));
        Assert.Equal(["region-or-score"], index.SnapshotMatches(new SubjectA(Region: "B", Score: 90.0)));
    }

    [Fact]
    public void TypedIdx_ForEachMatch_StopsWhenVisitorReturnsFalse()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("first", FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("A")));
        index.Add("second", FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("A")));
        var visited = new List<string>();

        bool Visitor(string subscription, ref List<string> state)
        {
            state.Add(subscription);
            return false;
        }

        index.ForEachMatch(new SubjectA(Region: "A"), ref visited, Visitor);

        Assert.Single(visited);
    }

    [Fact]
    public void TypedIdx_NestedIndexedField_DoesNotThrowWhenParentIsNull()
    {
        FilterSchema.RegisterValueObject<NestedLocation>();
        var index = new TypedFilterSubscriptionIndex<string, NestedSubject>();
        index.Add("vienna", FilterExpression.Compare(
            "Location.Country",
            FilterOperator.Equal,
            FilterValue.From("AT")));

        Assert.Empty(index.SnapshotCandidates(new NestedSubject(null!)));
        Assert.Empty(index.SnapshotMatches(new NestedSubject(null!)));
        Assert.Equal(["vienna"], index.SnapshotMatches(new NestedSubject(new NestedLocation("AT"))));
    }

    private sealed record SubjectA(string Region = "", double Score = 0.0) : IFilterSubject;

    private sealed record NestedSubject(NestedLocation Location) : IFilterSubject;

    private sealed record NestedLocation(string Country);
}

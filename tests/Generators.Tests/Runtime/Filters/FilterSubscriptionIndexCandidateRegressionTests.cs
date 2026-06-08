using SiftQL.Expressions;
using SiftQL.Index;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexCandidateRegressionTests
{
    [Fact]
    public void UntypedCandidateApisIgnoreSubjectsOfTheWrongRuntimeType()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(CandidateSubject));
        index.Add("all", FilterExpression.Any);
        index.Add(
            "high",
            FilterExpression.Compare(
                nameof(CandidateSubject.Id),
                FilterOperator.GreaterThan,
                FilterValue.From(10L)));
        List<string> visited = [];

        bool Visitor(string subscription, ref List<string> state)
        {
            state.Add(subscription);
            return true;
        }

        index.ForEachCandidate(new object(), ref visited, Visitor);

        Assert.Empty(visited);
        Assert.Empty(index.SnapshotCandidates(new object()));
        Assert.Equal(
            ["all", "high"],
            index.SnapshotCandidates(new CandidateSubject(20)).Order().ToArray());
    }

    private sealed record CandidateSubject(int Id) : IFilterSubject;
}

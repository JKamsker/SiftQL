using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexMutationRegressionTests
{
    [Fact]
    public void SchemaRefreshRebuildsFromAddedFilterSnapshot()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(MutationSubject));
        FilterExpression filter = FilterExpression.In(
            nameof(MutationSubject.Id),
            [FilterValue.From(100L)]);
        index.Add("sub", filter);

        filter.Values[0] = FilterValue.From(200L);

        Assert.Equal(["sub"], index.SnapshotMatches(new MutationSubject(100)));
        Assert.Empty(index.SnapshotMatches(new MutationSubject(200)));

        FilterSchema.RegisterValueObject<MutationMarker>();

        Assert.Equal(["sub"], index.SnapshotMatches(new MutationSubject(100)));
        Assert.Empty(index.SnapshotMatches(new MutationSubject(200)));
    }

    private sealed record MutationSubject(int Id) : IFilterSubject;

    private sealed record MutationMarker(int Value);
}

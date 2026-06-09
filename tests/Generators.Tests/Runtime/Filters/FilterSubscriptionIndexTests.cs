using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexTests
{
    private sealed record SubjectA(
        int Id = 0,
        string Region = "",
        bool Flag = false,
        long LargeId = 0,
        double Score = 0.0,
        float FloatScore = 0f,
        decimal Price = 0m,
        ulong ULargeId = 0,
        byte ByteVal = 0,
        Guid Token = default,
        SubjectStatus Status = SubjectStatus.None) : IFilterSubject;

    public enum SubjectStatus { None = 0, Active = 1, Suspended = 2 }

    [Fact]
    public void FSubIdx_Remove_IndexedSub_DecrementsCount()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("sub-a", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        Assert.Equal(1, index.Count);
        index.Remove("sub-a");
        Assert.Equal(0, index.Count);
        Assert.Empty(index.SnapshotCandidates(new SubjectA(Id: 10)));
    }

    [Fact]
    public void FSubIdx_Remove_IndexedSub_OtherSubUnaffected()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("sub-a", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        index.Add("sub-b", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        index.Remove("sub-a");
        Assert.Equal(1, index.Count);
        Assert.Equal(["sub-b"], index.SnapshotCandidates(new SubjectA(Id: 10)));
    }

    [Fact]
    public void FSubIdx_Remove_LastEntryForValue_ClearsEntry()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("sub-x", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(99L)));
        index.Remove("sub-x");
        Assert.Equal(0, index.Count);
        Assert.Empty(index.SnapshotCandidates(new SubjectA(Id: 99)));
    }

    [Fact]
    public void FSubIdx_Remove_UnindexedSub_DecrementsCount()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("all", null);
        index.Remove("all");
        Assert.Equal(0, index.Count);
        Assert.Empty(index.SnapshotCandidates(new SubjectA()));
    }

    [Fact]
    public void FSubIdx_Remove_UnindexedSub_OnlyOneOfTwo()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("a1", null);
        index.Add("a2", null);
        index.Remove("a1");
        Assert.Equal(1, index.Count);
        Assert.Equal(["a2"], index.SnapshotCandidates(new SubjectA()));
    }

    [Fact]
    public void FSubIdx_Remove_Nonexistent_IsNoOp()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("present", null);
        index.Remove("ghost");
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void FSubIdx_Remove_Nonexistent_EmptyIndex_IsNoOp()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Remove("ghost");
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void FSubIdx_ForEachCandidate_VisitsBothUnindexedAndIndexed()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("all", null);
        index.Add("id10", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        var visited = new List<string>();
        bool Visitor(string sub, ref List<string> state) { state.Add(sub); return true; }
        index.ForEachCandidate(new SubjectA(Id: 10), ref visited, Visitor);
        Assert.Contains("all", visited);
        Assert.Contains("id10", visited);
    }

    [Fact]
    public void FSubIdx_ForEachCandidate_EarlyReturn_StopsIteration()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("a", null);
        index.Add("b", null);
        var visited = new List<string>();
        bool Visitor(string sub, ref List<string> state) { state.Add(sub); return false; }
        index.ForEachCandidate(new SubjectA(), ref visited, Visitor);
        Assert.Single(visited);
    }

    [Fact]
    public void FSubIdx_ForEachCandidate_NoMatches_VisitsNothing()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("id99", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(99L)));
        var visited = new List<string>();
        bool Visitor(string sub, ref List<string> state) { state.Add(sub); return true; }
        index.ForEachCandidate(new SubjectA(Id: 1), ref visited, Visitor);
        Assert.Empty(visited);
    }

    [Fact]
    public void FSubIdx_ForEachCandidate_EmptyIndex_VisitsNothing()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        var visited = new List<string>();
        bool Visitor(string sub, ref List<string> state) { state.Add(sub); return true; }
        index.ForEachCandidate(new SubjectA(), ref visited, Visitor);
        Assert.Empty(visited);
    }

    [Fact]
    public void FSubIdx_SnapshotCandidates_IncludesBothUnindexedAndMatchingIndexed()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("all", null);
        index.Add("id5", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        index.Add("id99", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(99L)));
        var candidates = index.SnapshotCandidates(new SubjectA(Id: 5));
        Assert.Contains("all", candidates);
        Assert.Contains("id5", candidates);
        Assert.DoesNotContain("id99", candidates);
    }

    [Fact]
    public void FSubIdx_SnapshotCandidates_WrongRuntimeType_ReturnsEmpty()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("all", null);
        index.Add("id5", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        var candidates = index.SnapshotCandidates(new object());
        Assert.Empty(candidates);
    }

    [Fact]
    public void TypedIdx_Count_ReflectsIndexedAndUnindexed()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("indexed", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)));
        index.Add("unindexed", null);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void TypedIdx_Remove_Nonexistent_DoesNotChangeCount()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("present", null);
        index.Remove("ghost");
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void TypedIdx_Remove_IndexedEntry_RemovesFromFieldSnapshot()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("sub", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(7L)));
        Assert.Equal(1, index.Count);
        index.Remove("sub");
        Assert.Equal(0, index.Count);
        Assert.Empty(index.SnapshotCandidates(new SubjectA(Id: 7)));
    }

    [Fact]
    public void TypedIdx_Remove_UnindexedEntry_DecrementsCount()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("all", null);
        index.Remove("all");
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void TypedIdx_ForEachCandidate_EarlyExitOnUnindexed()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("first", null);
        index.Add("second", null);
        var visited = new List<string>();
        bool Visitor(string sub, ref List<string> state) { state.Add(sub); return false; }
        index.ForEachCandidate(new SubjectA(), ref visited, Visitor);
        Assert.Single(visited);
    }

    [Fact]
    public void TypedIdx_ForEachCandidate_EarlyExitOnIndexedMatch()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("indexed-1", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)));
        index.Add("indexed-2", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)));
        int callCount = 0;
        bool Visitor(string sub, ref int count) { count++; return false; }
        index.ForEachCandidate(new SubjectA(Id: 1), ref callCount, Visitor);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void TypedIdx_SnapshotCandidates_NoMatch_ReturnsUnindexedOnly()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("all", null);
        index.Add("id10", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        string[] candidates = index.SnapshotCandidates(new SubjectA(Id: 99));
        Assert.Equal(["all"], candidates);
    }

    [Fact]
    public void TypedIdx_SnapshotCandidates_Empty_ReturnsEmpty()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        Assert.Empty(index.SnapshotCandidates(new SubjectA()));
    }

    [Fact]
    public void TypedIdx_Count_AfterRemovingAllIndexed_IsZero()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("s1", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)));
        index.Add("s2", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(2L)));
        index.Remove("s1");
        index.Remove("s2");
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void TypedIdx_ForEachCandidate_NoMatches_NoVisits()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("id5", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        int visits = 0;
        bool Visitor(string sub, ref int count) { count++; return true; }
        index.ForEachCandidate(new SubjectA(Id: 99), ref visits, Visitor);
        Assert.Equal(0, visits);
    }

    [Fact]
    public void FSubIdx_Remove_LastSubscription_ClearsFieldIndexFromInternalDictionary()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("sub-1", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        index.Remove("sub-1");

        var fieldsField = typeof(FilterSubscriptionIndex<string>)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fields = (System.Collections.IDictionary)fieldsField!.GetValue(index)!;

        Assert.Empty(fields);
    }

    [Fact]
    public void TypedIdx_Remove_LastSubscription_ClearsFieldIndexFromInternalDictionary()
    {
        var index = new TypedFilterSubscriptionIndex<string, SubjectA>();
        index.Add("sub-1", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(10L)));
        index.Remove("sub-1");

        var fieldsField = typeof(TypedFilterSubscriptionIndex<string, SubjectA>)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fields = (System.Collections.IDictionary)fieldsField!.GetValue(index)!;

        Assert.Empty(fields);
    }
}

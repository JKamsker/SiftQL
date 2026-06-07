using MessagePack.Resolvers;
using MessagePack;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave4CoverageTests
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

    private sealed class FakeProjection { }

    // ===== 1. ProjectionMatchAccumulator =====

    [Fact]
    public void Accumulator_Empty_IsEmpty()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        Assert.True(acc.IsEmpty);
        Assert.Equal(0, acc.GroupCount);
        Assert.Empty(acc.ToArray());
    }

    [Fact]
    public void Accumulator_Empty_Enumerator_YieldsNothing()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        var e = acc.GetEnumerator();
        Assert.False(e.MoveNext());
    }

    [Fact]
    public void Accumulator_SingleEntry_OneGroup()
    {
        var proj = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("sub-1", "key-A", proj);
        Assert.False(acc.IsEmpty);
        Assert.Equal(1, acc.GroupCount);
        var groups = acc.ToArray();
        Assert.Single(groups);
        Assert.Same(proj, groups[0].Projection);
        Assert.Equal(1, groups[0].SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_SameKey_AccumulatesInOneGroup()
    {
        var proj = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("sub-1", "key-A", proj);
        acc.Add("sub-2", "key-A", proj);
        acc.Add("sub-3", "key-A", proj);
        Assert.Equal(1, acc.GroupCount);
        Assert.Equal(3, acc.ToArray().Single().SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_FourDistinctKeys_FourInlineGroups()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("s1", "key-1", new FakeProjection());
        acc.Add("s2", "key-2", new FakeProjection());
        acc.Add("s3", "key-3", new FakeProjection());
        acc.Add("s4", "key-4", new FakeProjection());
        Assert.Equal(4, acc.GroupCount);
        Assert.Equal(4, acc.ToArray().Length);
    }

    [Fact]
    public void Accumulator_FifthKey_OverflowsToDictionary()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        for (int i = 0; i < 5; i++)
            acc.Add($"sub-{i}", $"key-{i}", new FakeProjection());
        Assert.Equal(5, acc.GroupCount);
        Assert.Equal(5, acc.ToArray().Length);
    }

    [Fact]
    public void Accumulator_SixDistinctKeys_OverflowTwo()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        for (int i = 0; i < 6; i++)
            acc.Add($"sub-{i}", $"key-{i}", new FakeProjection());
        Assert.Equal(6, acc.GroupCount);
        Assert.Equal(6, acc.ToArray().Length);
    }

    [Fact]
    public void Accumulator_OverflowGroup_ReceivesSecondSubscription()
    {
        var proj = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        for (int i = 0; i < 4; i++)
            acc.Add($"sub-{i}", $"key-{i}", new FakeProjection());
        acc.Add("ov-1", "key-ov", proj);
        acc.Add("ov-2", "key-ov", proj);
        Assert.Equal(5, acc.GroupCount);
        var ov = acc.ToArray().First(g => ReferenceEquals(g.Projection, proj));
        Assert.Equal(2, ov.SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_GroupWithFiveSubIds_UsesExtraList()
    {
        var proj = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        for (int i = 0; i < 5; i++)
            acc.Add($"sub-{i}", "shared", proj);
        Assert.Equal(1, acc.GroupCount);
        Assert.Equal(5, acc.ToArray().Single().SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_Enumerator_VisitsAllGroupsIncludingOverflow()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        for (int i = 0; i < 6; i++)
            acc.Add($"sub-{i}", $"key-{i}", new FakeProjection());
        int seen = 0;
        foreach (var _ in acc)
            seen++;
        Assert.Equal(6, seen);
    }

    [Fact]
    public void Accumulator_SecondGroupMatch_AddsToSecondSlot()
    {
        var proj2 = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("s1", "key-1", new FakeProjection());
        acc.Add("s2", "key-2", proj2);
        acc.Add("s3", "key-2", proj2);
        Assert.Equal(2, acc.GroupCount);
        var g2 = acc.ToArray().First(g => ReferenceEquals(g.Projection, proj2));
        Assert.Equal(2, g2.SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_ThirdGroupMatch_AddsToThirdSlot()
    {
        var proj3 = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("s1", "key-1", new FakeProjection());
        acc.Add("s2", "key-2", new FakeProjection());
        acc.Add("s3", "key-3", proj3);
        acc.Add("s4", "key-3", proj3);
        Assert.Equal(3, acc.GroupCount);
        var g3 = acc.ToArray().First(g => ReferenceEquals(g.Projection, proj3));
        Assert.Equal(2, g3.SubscriptionIds.Count);
    }

    [Fact]
    public void Accumulator_FourthGroupMatch_AddsToFourthSlot()
    {
        var proj4 = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("s1", "key-1", new FakeProjection());
        acc.Add("s2", "key-2", new FakeProjection());
        acc.Add("s3", "key-3", new FakeProjection());
        acc.Add("s4", "key-4", proj4);
        acc.Add("s5", "key-4", proj4);
        Assert.Equal(4, acc.GroupCount);
        var g4 = acc.ToArray().First(g => ReferenceEquals(g.Projection, proj4));
        Assert.Equal(2, g4.SubscriptionIds.Count);
    }

    // ===== 2. ProjectionDispatchGroup =====

    [Fact]
    public void DispatchGroup_SingleSubscription_IteratesCorrectly()
    {
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        acc.Add("my-sub", "k1", new FakeProjection());
        var group = acc.ToArray().Single();
        Assert.Equal(1, group.SubscriptionIds.Count);
        int count = 0;
        for (int i = 0; i < group.SubscriptionIds.Count; i++) { count++; Assert.Equal("my-sub", group.SubscriptionIds[i]); }
        Assert.Equal(1, count);
    }

    [Fact]
    public void DispatchGroup_FiveSubscriptions_AllPresentInIteration()
    {
        var proj = new FakeProjection();
        var acc = new ProjectionMatchAccumulator<FakeProjection>();
        string[] expected = ["a", "b", "c", "d", "e"];
        foreach (string id in expected) acc.Add(id, "shared", proj);
        var group = acc.ToArray().Single();
        Assert.Equal(5, group.SubscriptionIds.Count);
        var actual = new List<string>();
        for (int i = 0; i < group.SubscriptionIds.Count; i++) actual.Add(group.SubscriptionIds[i]);
        Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
    }

    // ===== 3. FilterSubscriptionIndex<T> =====

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
    public void FSubIdx_SnapshotCandidates_WrongRuntimeType_ReturnsOnlyUnindexed()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(SubjectA));
        index.Add("all", null);
        index.Add("id5", FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        var candidates = index.SnapshotCandidates(new object());
        Assert.Equal(["all"], candidates);
    }

    // ===== 4. TypedFilterSubscriptionIndex =====

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

    // ===== 5. FilterIndexExtractor =====

    [Fact]
    public void Extractor_NullExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), null));

    [Fact]
    public void Extractor_AnyExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.Any));

    [Fact]
    public void Extractor_SimpleEqual_ReturnsKey()
    {
        var expr = FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(42L));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(nameof(SubjectA.Id), key!.Field, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(42L, key.Value.Integer);
    }

    [Fact]
    public void Extractor_NotEqualOperator_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.NotEqual, FilterValue.From(1L))));

    [Fact]
    public void Extractor_GreaterThanOperator_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.GreaterThan, FilterValue.From(1L))));

    [Fact]
    public void Extractor_And_SelectsMostSelectiveKey()
    {
        // "Id" ends with "Id" -> score 0 (most selective); "Region" -> score 50
        var expr = FilterExpression.And(
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("north")),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(nameof(SubjectA.Id), key!.Field, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extractor_And_AllNonIndexable_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.And(
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.GreaterThan, FilterValue.From(0L)),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.LessThan, FilterValue.From(100L)))));

    [Fact]
    public void Extractor_DecimalField_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Price), FilterOperator.Equal, FilterValue.From(1.5m))));

    [Fact]
    public void Extractor_EnumStringValue_ReturnsKey()
    {
        var expr = FilterExpression.Compare(nameof(SubjectA.Status), FilterOperator.Equal, FilterValue.From(nameof(SubjectStatus.Active)));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(1L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_EnumIntValue_ReturnsKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Status), FilterOperator.Equal, FilterValue.From(2L)));
        Assert.NotNull(key);
        Assert.Equal(2L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_DoubleField_NumberKind_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Score), FilterOperator.Equal, FilterValue.From(3.14))));

    [Fact]
    public void Extractor_ULargeId_NegativeInteger_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.ULargeId), FilterOperator.Equal, FilterValue.From(-1L))));

    [Fact]
    public void Extractor_LongField_IntegerKind_ReturnsKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.LargeId), FilterOperator.Equal, FilterValue.From(12345L)));
        Assert.NotNull(key);
        Assert.Equal(12345L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_StringField_ReturnsStringKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("west")));
        Assert.NotNull(key);
        Assert.Equal("west", key!.Value.String);
    }

    [Fact]
    public void Extractor_GuidField_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Token), FilterOperator.Equal, FilterValue.From(Guid.NewGuid()))));

    [Fact]
    public void Extractor_OrExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.Or(
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(2L)))));

    [Fact]
    public void Extractor_UnsignedSmallValue_ReturnsIntegerKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(100UL)));
        Assert.NotNull(key);
        Assert.Equal(100L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_BooleanField_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Flag), FilterOperator.Equal, FilterValue.From(true))));

    [Fact]
    public void Extractor_FloatField_IntegerKind_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.FloatScore), FilterOperator.Equal, FilterValue.From(5L))));

    // ===== 6. ProjectionIncludeArguments =====

    private static EventProjectionInclude MakeInclude(string intrinsic, params EventProjectionArgument[] args) =>
        new(intrinsic, "result", args);

    [Fact]
    public void IncludeArgs_RequiredString_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("name", FilterValue.From("hello")));
        Assert.Equal("hello", ProjectionIncludeArguments.RequiredString(include, "name"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_CaseInsensitive()
    {
        var include = MakeInclude("op", new EventProjectionArgument("Name", FilterValue.From("world")));
        Assert.Equal("world", ProjectionIncludeArguments.RequiredString(include, "name"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredString(MakeInclude("op"), "missing"));

    [Fact]
    public void IncludeArgs_RequiredString_WrongType_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("num", FilterValue.From(42L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredString(include, "num"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_WhitespaceOnly_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("ws", FilterValue.From("   ")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredString(include, "ws"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_CustomErrorFactory_UsedOnMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredString(MakeInclude("op"), "x", msg => new InvalidOperationException(msg)));
        Assert.Contains("missing argument", ex.Message);
    }

    [Fact]
    public void IncludeArgs_RequiredString_CustomErrorFactory_UsedOnWrongType()
    {
        var include = MakeInclude("op", new EventProjectionArgument("n", FilterValue.From(1L)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredString(include, "n", msg => new InvalidOperationException(msg)));
        Assert.Contains("must be a string", ex.Message);
    }

    [Fact]
    public void IncludeArgs_RequiredInt_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("count", FilterValue.From(7L)));
        Assert.Equal(7, ProjectionIncludeArguments.RequiredInt(include, "count"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredInt(MakeInclude("op"), "n"));

    [Fact]
    public void IncludeArgs_RequiredInt_WrongKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("val", FilterValue.From("not-int")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "val"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_TooBig_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("big", FilterValue.From((long)int.MaxValue + 1L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "big"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_TooSmall_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("small", FilterValue.From((long)int.MinValue - 1L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "small"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_IntegerKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(5L)));
        Assert.Equal(5.0, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_NumberKind_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(3.14)));
        Assert.Equal(3.14, ProjectionIncludeArguments.RequiredDouble(include, "d"), 10);
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_UnsignedIntegerKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(100UL)));
        Assert.Equal(100.0, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_DecimalKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(2.5m)));
        Assert.Equal(2.5, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredDouble(MakeInclude("op"), "d"));

    [Fact]
    public void IncludeArgs_RequiredDouble_StringKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From("bad")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_BoolKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(true)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_CustomErrorFactory_OnWrongType()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(true)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredDouble(include, "d", msg => new InvalidOperationException(msg)));
        Assert.Contains("must be a number", ex.Message);
    }

    // ===== 7. ProjectionPayloadWriterCompiler via CompiledProjection =====

    private static MessagePackSerializerOptions PayloadOptions { get; } =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private sealed record PayloadSubject(
        bool Flag = false,
        bool? NullableFlag = null,
        int IntVal = 0,
        int? NullableInt = null,
        long LongVal = 0L,
        ulong ULongVal = 0UL,
        float FloatVal = 0f,
        double DoubleVal = 0.0,
        decimal DecimalVal = 0m,
        string? StringVal = null,
        Guid GuidVal = default) : IFilterSubject;

    private static CompiledProjection<object> CompilePayloadProjection(params string[] fields) =>
        ProjectionCompiler.Compile<object>(
            typeof(PayloadSubject),
            EventProjectionExpression.Select(fields),
            static (_, inc) => throw new InvalidOperationException());

    private static async Task<ProjectedEvent> RoundTripAsync(CompiledProjection<object> proj, PayloadSubject subject)
    {
        ReadOnlyMemory<byte> payload = await proj.ProjectPayloadAsync(
            subject, new object(), PayloadOptions, CancellationToken.None);
        return MessagePackSerializer.Deserialize<ProjectedEvent>(payload, PayloadOptions);
    }

    [Fact]
    public async Task PayloadWriter_BoolTrue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.Flag));
        var result = await RoundTripAsync(proj, new PayloadSubject(Flag: true));
        Assert.True(result.TryGetField(nameof(PayloadSubject.Flag), out var val));
        Assert.Equal(ProjectedEventValueKind.Boolean, val.Kind);
        Assert.True(val.Boolean);
    }

    [Fact]
    public async Task PayloadWriter_NullableBool_Null_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableFlag));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableFlag: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableFlag), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_NullableBool_HasValue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableFlag));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableFlag: false));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableFlag), out var val));
        Assert.Equal(ProjectedEventValueKind.Boolean, val.Kind);
        Assert.False(val.Boolean);
    }

    [Fact]
    public async Task PayloadWriter_IntField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.IntVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(IntVal: 42));
        Assert.True(result.TryGetField(nameof(PayloadSubject.IntVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(42L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_NullableInt_Null_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableInt));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableInt: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableInt), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_NullableInt_HasValue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableInt));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableInt: 99));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableInt), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(99L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_LongField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.LongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(LongVal: 9999L));
        Assert.True(result.TryGetField(nameof(PayloadSubject.LongVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(9999L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_FloatField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.FloatVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(FloatVal: 1.5f));
        Assert.True(result.TryGetField(nameof(PayloadSubject.FloatVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Number, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_DoubleField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DoubleVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DoubleVal: 2.71));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DoubleVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Number, val.Kind);
        Assert.Equal(2.71, val.Number, 5);
    }

    [Fact]
    public async Task PayloadWriter_DecimalIntegral_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DecimalVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DecimalVal: 10m));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DecimalVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(10L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_DecimalFractional_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DecimalVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DecimalVal: 1.5m));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DecimalVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Decimal, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_ULongSmall_WrittenAsInteger()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.ULongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(ULongVal: 100UL));
        Assert.True(result.TryGetField(nameof(PayloadSubject.ULongVal), out var val));
        Assert.Equal(100L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_ULongBig_WrittenAsUnsignedInteger()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var proj = CompilePayloadProjection(nameof(PayloadSubject.ULongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(ULongVal: big));
        Assert.True(result.TryGetField(nameof(PayloadSubject.ULongVal), out var val));
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, val.Kind);
        Assert.Equal(big, val.UnsignedInteger);
    }

    [Fact]
    public async Task PayloadWriter_StringNonNull_WrittenAsString()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.StringVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(StringVal: "hello"));
        Assert.True(result.TryGetField(nameof(PayloadSubject.StringVal), out var val));
        Assert.Equal("hello", val.String);
    }

    [Fact]
    public async Task PayloadWriter_StringNull_WrittenAsNull()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.StringVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(StringVal: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.StringVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_GuidField_WrittenAsGuid()
    {
        var g = Guid.NewGuid();
        var proj = CompilePayloadProjection(nameof(PayloadSubject.GuidVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(GuidVal: g));
        Assert.True(result.TryGetField(nameof(PayloadSubject.GuidVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Guid, val.Kind);
        Assert.Equal(g, val.Guid);
    }

    // ===== 8. CompiledEventPipeline.ProjectPayloadAsync =====

    private sealed record PipeSubject(int Id = 0, string Name = "", bool Active = false) : IFilterSubject;

    private static CompiledEventPipeline<object> CompilePipeline(
        Type subjectType,
        EventPipelineExpression? pipeline = null) =>
        EventPipelineCompiler.Compile<object>(
            subjectType, pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_NoFilter_ProducesPayload()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        ReadOnlyMemory<byte>? payload = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 5, Name: "test"), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(payload);
        Assert.NotNull(MessagePackSerializer.Deserialize<ProjectedEvent>(payload!.Value, PayloadOptions));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_FilterRejects_ReturnsNull()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Id), FilterOperator.Equal, FilterValue.From(1L)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var matched = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 1), new object(), PayloadOptions, CancellationToken.None);
        var rejected = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 2), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(matched);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task Pipeline_ProjectAsync_NullSubject_Throws()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await pipeline.ProjectAsync(null!, new object(), CancellationToken.None));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_NullSubject_Throws()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await pipeline.ProjectPayloadAsync(null!, new object(), PayloadOptions, CancellationToken.None));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_FieldSelection_ProducesExpectedFields()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(PipeSubject.Name)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var payload = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 99, Name: "Alice", Active: true),
            new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(payload);
        var ev = MessagePackSerializer.Deserialize<ProjectedEvent>(payload!.Value, PayloadOptions);
        Assert.True(ev.TryGetField(nameof(PipeSubject.Name), out var nameField));
        Assert.Equal("Alice", nameField.String);
    }

    [Fact]
    public void Pipeline_Key_ContainsSubjectTypeToken()
        => Assert.Contains("subject:", CompilePipeline(typeof(PipeSubject)).Key);

    [Fact]
    public async Task Pipeline_FilterThenSelect_MatchingSubject_ProducesPayload()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Active), FilterOperator.Equal, FilterValue.From(true)))
            .AppendProjection(EventProjectionExpression.Select(nameof(PipeSubject.Name)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var active = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Name: "Bob", Active: true), new object(), PayloadOptions, CancellationToken.None);
        var inactive = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Name: "Eve", Active: false), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(active);
        Assert.Null(inactive);
    }

    // ===== 9. EventPipelineCompiler cache / utility methods =====

    [Fact]
    public void EventPipelineCompiler_SameArgs_ReturnsCachedInstance()
    {
        var first = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        var second = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        Assert.Same(first, second);
    }

    [Fact]
    public void EventPipelineCompiler_DifferentSubjectTypes_ReturnDifferentInstances()
    {
        var forPipe = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        var forA = EventPipelineCompiler.Compile<object>(
            typeof(SubjectA), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        Assert.NotSame(forPipe, forA);
    }

    [Fact]
    public void EventPipelineCompiler_SourceFilter_NullPipeline_ReturnsAnyExpression()
        => Assert.Equal(FilterExpressionKind.Any, EventPipelineCompiler.SourceFilter(null).Kind);

    [Fact]
    public void EventPipelineCompiler_SourceFilter_PipelineWithPreFilter_ReturnsNonAnyFilter()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Id), FilterOperator.Equal, FilterValue.From(1L)))
            .AppendProjection(EventProjectionExpression.Default);
        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(pipeline);
        Assert.NotEqual(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_NullPipeline_ReturnsProjectedPipeline()
    {
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(null);
        Assert.NotNull(dispatched);
        Assert.True(dispatched.HasProjection);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_PreFilterStripped()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Active), FilterOperator.Equal, FilterValue.From(true)))
            .AppendProjection(EventProjectionExpression.Default);
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        Assert.True(dispatched.Stages.Length < pipeline.Stages.Length);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_NoPreFilter_SameStageCount()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default);
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        Assert.Equal(pipeline.Stages.Length, dispatched.Stages.Length);
    }
}

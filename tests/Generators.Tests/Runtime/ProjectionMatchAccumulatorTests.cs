using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionMatchAccumulatorTests
{
    private sealed class FakeProjection { }

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
}

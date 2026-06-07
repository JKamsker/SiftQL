using SiftQL;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubscriptionIdBatchTests
{
    [Fact]
    public void SubscriptionIdBatch_One_AccessFirstSlot()
    {
        SubscriptionIdBatch batch = SubscriptionIdBatch.One("sub-1");
        Assert.Equal(1, batch.Count);
        Assert.Equal("sub-1", batch[0]);
    }

    [Fact]
    public void SubscriptionIdBatch_FourSlots_AccessAll()
    {
        var batch = new SubscriptionIdBatch(4, "a", "b", "c", "d");
        Assert.Equal("a", batch[0]);
        Assert.Equal("b", batch[1]);
        Assert.Equal("c", batch[2]);
        Assert.Equal("d", batch[3]);
    }

    [Fact]
    public void SubscriptionIdBatch_Overflow_AccessOverflowSlot()
    {
        var batch = new SubscriptionIdBatch(6, "a", "b", "c", "d", ["e", "f"]);
        Assert.Equal("e", batch[4]);
        Assert.Equal("f", batch[5]);
    }

    [Fact]
    public void SubscriptionIdBatch_OutOfRange_Throws()
    {
        var batch = SubscriptionIdBatch.One("sub-1");
        Assert.Throws<ArgumentOutOfRangeException>(() => batch[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => batch[-1]);
    }

    [Fact]
    public void SubscriptionIdBatch_ToArray_ReturnsAllIds()
    {
        var batch = new SubscriptionIdBatch(6, "a", "b", "c", "d", ["e", "f"]);
        string[] array = batch.ToArray();
        Assert.Equal(["a", "b", "c", "d", "e", "f"], array);
    }

    [Fact]
    public void SubscriptionIdBatch_OverflowMissing_ThrowsOnAccess()
    {
        var batch = new SubscriptionIdBatch(5, "a", "b", "c", "d", Overflow: null);
        Assert.Throws<InvalidOperationException>(() => batch[4]);
    }
}

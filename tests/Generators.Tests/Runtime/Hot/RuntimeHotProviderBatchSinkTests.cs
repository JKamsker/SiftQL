using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class RuntimeHotProviderBatchSinkTests
{
    [Fact]
    public async Task BatchSinkForwardsAndQueuesWhenThresholdIsReached()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        var inner = new RuntimeHotProviderBatchTestSupport.RecordingManifestSink();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 2,
                MinimumInterval = TimeSpan.Zero,
            },
            inner);

        sink.RecordHotFilter(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Filter(), 12, 3);
        sink.RecordHotProjection(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Projection(), 7, 2);

        RuntimeHotProviderBatch batch = await queue.WaitAsync();
        Assert.Equal(1, inner.FilterCalls);
        Assert.Equal(1, inner.ProjectionCalls);
        Assert.Equal(2, batch.Entries.Length);
        Assert.Contains(batch.Entries, static entry => entry.Kind == "filter");
        Assert.Contains(batch.Entries, static entry => entry.Kind == "projection");
    }

    [Fact]
    public async Task BatchSinkWaitsForMinimumEntries()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 2,
                MinimumInterval = TimeSpan.Zero,
            });

        sink.RecordHotFilter(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Filter(), 12, 3);
        await Task.Delay(50);

        Assert.False(queue.HasBatch);
    }

    [Fact]
    public async Task BatchSinkQueuesDelayedBatchWhenThresholdIsReachedDuringCooldown()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.FromMilliseconds(100),
            });

        sink.RecordHotFilter(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Filter(), 12, 3);
        await queue.WaitAsync();
        sink.RecordHotProjection(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Projection(), 7, 2);

        RuntimeHotProviderBatch delayed = await queue.WaitAsync();

        Assert.Single(delayed.Entries);
        Assert.Equal("projection", delayed.Entries[0].Kind);
    }

    [Fact]
    public async Task BatchSinkDrainsCooldownBacklogWithoutWaitingForAnotherRecord()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 2,
                MaxEntries = 2,
                MinimumInterval = TimeSpan.FromMilliseconds(50),
            });

        sink.RecordHotFilter(typeof(RuntimeHotProviderBatchTestSupport.FirstHotSubject), RuntimeHotProviderBatchTestSupport.Filter(), 1, 1);
        sink.RecordHotFilter(typeof(RuntimeHotProviderBatchTestSupport.SecondHotSubject), RuntimeHotProviderBatchTestSupport.Filter(), 1, 1);
        await queue.WaitAsync();

        sink.RecordHotFilter(typeof(RuntimeHotProviderBatchTestSupport.FirstHotSubject), RuntimeHotProviderBatchTestSupport.Filter(), 2, 1);
        sink.RecordHotFilter(typeof(RuntimeHotProviderBatchTestSupport.SecondHotSubject), RuntimeHotProviderBatchTestSupport.Filter(), 2, 1);
        sink.RecordHotProjection(typeof(RuntimeHotProviderBatchTestSupport.FirstHotSubject), RuntimeHotProviderBatchTestSupport.Projection(), 1, 1);
        sink.RecordHotProjection(typeof(RuntimeHotProviderBatchTestSupport.SecondHotSubject), RuntimeHotProviderBatchTestSupport.Projection(), 1, 1);

        await queue.WaitAsync();
        RuntimeHotProviderBatch backlog = await queue.WaitAsync();

        Assert.Equal(2, backlog.Entries.Length);
    }

    [Fact]
    public async Task BatchSinkRetriesRequeuedBatchWithoutWaitingForAnotherRecord()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.ThrowOnceBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.Zero,
            });

        sink.RecordHotFilter(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Filter(), 12, 3);

        RuntimeHotProviderBatch batch = await queue.WaitAsync();
        Assert.Single(batch.Entries);
        Assert.Equal("filter", batch.Entries[0].Kind);
        Assert.Equal(2, queue.Attempts);
    }

    [Fact]
    public async Task BatchSinkStillQueuesWhenInnerSinkThrows()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.Zero,
            },
            new RuntimeHotProviderBatchTestSupport.ThrowOnceSink(throwFilters: true));

        Exception? exception = Record.Exception(() =>
            sink.RecordHotFilter(typeof(ItemUsedEvent), RuntimeHotProviderBatchTestSupport.Filter(), 12, 3));

        Assert.Null(exception);
        RuntimeHotProviderBatch batch = await queue.WaitAsync();
        Assert.Single(batch.Entries);
        Assert.Equal("filter", batch.Entries[0].Kind);
    }
}

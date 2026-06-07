using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class RuntimeHotProviderBatchSinkTests
{
    public static void RunAll()
    {
        BatchSinkForwardsAndQueuesWhenThresholdIsReached().GetAwaiter().GetResult();
        BatchSinkWaitsForMinimumEntries().GetAwaiter().GetResult();
        BatchSinkQueuesDelayedBatchWhenThresholdIsReachedDuringCooldown().GetAwaiter().GetResult();
        BatchSinkDrainsCooldownBacklogWithoutWaitingForAnotherRecord().GetAwaiter().GetResult();
        BatchSinkRetriesRequeuedBatchWithoutWaitingForAnotherRecord().GetAwaiter().GetResult();
        BatchSinkStillQueuesWhenInnerSinkThrows().GetAwaiter().GetResult();
    }

    private static async Task BatchSinkForwardsAndQueuesWhenThresholdIsReached()
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

    private static async Task BatchSinkWaitsForMinimumEntries()
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

    private static async Task BatchSinkQueuesDelayedBatchWhenThresholdIsReachedDuringCooldown()
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

    private static async Task BatchSinkDrainsCooldownBacklogWithoutWaitingForAnotherRecord()
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

    private static async Task BatchSinkRetriesRequeuedBatchWithoutWaitingForAnotherRecord()
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

    private static async Task BatchSinkStillQueuesWhenInnerSinkThrows()
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

using SiftQL.Expressions;
using SiftQL.Hot;

namespace SiftQL.Generators.Tests;

public sealed class RuntimeHotProviderBatchSinkNonFiniteRegressionTests
{
    [Fact]
    public async Task BatchSinkSkipsNonFiniteFilterNumbers()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.Zero,
            });
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(double.NaN));

        Exception? exception = Record.Exception(() =>
            sink.RecordHotFilter(typeof(ItemUsedEvent), filter, 1, 0));
        await Task.Delay(50);

        Assert.Null(exception);
        Assert.False(queue.HasBatch);
    }

    [Fact]
    public async Task BatchSinkSkipsNonFiniteProjectionArgument()
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.Zero,
            });
        EventProjectionExpression projection = EventProjectionExpression
            .Select(nameof(ItemUsedEvent.ItemId))
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "test.window",
                    "window",
                    new EventProjectionArgument("offset", FilterValue.From(double.NaN))),
            ]);

        Exception? exception = Record.Exception(() =>
            sink.RecordHotProjection(typeof(ItemUsedEvent), projection, 1, 0));
        await Task.Delay(50);

        Assert.Null(exception);
        Assert.False(queue.HasBatch);
    }
}

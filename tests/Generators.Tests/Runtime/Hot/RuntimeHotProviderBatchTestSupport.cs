using System.Threading.Channels;
using SiftQL.Expressions;
using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class RuntimeHotProviderBatchTestSupport
{
    public static FilterExpression Filter() =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));

    public static EventProjectionExpression Projection() =>
        EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));

    public sealed record FirstHotSubject(int ItemId);
    public sealed record SecondHotSubject(int ItemId);

    public sealed class RecordingBatchQueue : IRuntimeHotProviderBatchQueue
    {
        private readonly Channel<RuntimeHotProviderBatch> _batches =
            Channel.CreateUnbounded<RuntimeHotProviderBatch>();
        private int _queued;

        public bool HasBatch => Volatile.Read(ref _queued) > 0;

        public void Queue(RuntimeHotProviderBatch batch)
        {
            Interlocked.Increment(ref _queued);
            Assert.True(_batches.Writer.TryWrite(batch));
        }

        public async Task<RuntimeHotProviderBatch> WaitAsync()
        {
            Task<RuntimeHotProviderBatch> read = _batches.Reader.ReadAsync().AsTask();
            Task completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(read, completed);
            Interlocked.Decrement(ref _queued);
            return await read;
        }
    }

    public sealed class RecordingManifestSink : ITieredHotManifestSink
    {
        public int FilterCalls { get; private set; }
        public int ProjectionCalls { get; private set; }

        public void RecordHotFilter(
            Type subjectType,
            FilterExpression expression,
            long evaluations,
            long matches)
        {
            _ = subjectType;
            _ = expression;
            _ = evaluations;
            _ = matches;
            FilterCalls++;
        }

        public void RecordHotProjection(
            Type subjectType,
            EventProjectionExpression projection,
            long materializations,
            long payloadWrites)
        {
            _ = subjectType;
            _ = projection;
            _ = materializations;
            _ = payloadWrites;
            ProjectionCalls++;
        }
    }

    public sealed class ThrowOnceBatchQueue : IRuntimeHotProviderBatchQueue
    {
        private readonly Channel<RuntimeHotProviderBatch> _batches =
            Channel.CreateUnbounded<RuntimeHotProviderBatch>();
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void Queue(RuntimeHotProviderBatch batch)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
                throw new InvalidOperationException("Transient queue failure.");

            Assert.True(_batches.Writer.TryWrite(batch));
        }

        public async Task<RuntimeHotProviderBatch> WaitAsync()
        {
            Task<RuntimeHotProviderBatch> read = _batches.Reader.ReadAsync().AsTask();
            Task completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(read, completed);
            return await read;
        }
    }

    public sealed class ThrowOnceSink(bool throwFilters) : ITieredHotManifestSink
    {
        private int _filterThrows;

        public void RecordHotFilter(
            Type subjectType,
            FilterExpression expression,
            long evaluations,
            long matches)
        {
            _ = subjectType;
            _ = expression;
            _ = evaluations;
            _ = matches;
            if (throwFilters && Interlocked.Exchange(ref _filterThrows, 1) == 0)
                throw new InvalidOperationException("Transient sink failure.");
        }

        public void RecordHotProjection(
            Type subjectType,
            EventProjectionExpression projection,
            long materializations,
            long payloadWrites)
        {
            _ = subjectType;
            _ = projection;
            _ = materializations;
            _ = payloadWrites;
        }
    }
}

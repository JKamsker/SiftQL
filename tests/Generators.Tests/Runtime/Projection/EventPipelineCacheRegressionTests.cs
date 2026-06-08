using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineCacheRegressionTests
{
    [Fact]
    public async Task TieredProjectionPipelinesDoNotSharePromotionStateThroughCache()
    {
        var sink = new CountingSink();
        var pipeline = EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)));
        var options = EventPipelineCompilerOptions.Immediate with
        {
            ProjectionOptions = ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 2,
                HotManifestSink = sink,
            },
        };

        CompiledEventPipeline<object> first = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            options);
        CompiledEventPipeline<object> second = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            options);

        Assert.NotSame(first, second);
        await first.ProjectAsync(Event(100), new object(), CancellationToken.None);
        await second.ProjectAsync(Event(200), new object(), CancellationToken.None);

        Assert.Equal(0, sink.ProjectionCalls);
    }

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: 2);

    private sealed class CountingSink : ITieredHotManifestSink
    {
        private int _projectionCalls;

        public int ProjectionCalls => Volatile.Read(ref _projectionCalls);

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
            Interlocked.Increment(ref _projectionCalls);
        }
    }
}

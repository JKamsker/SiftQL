using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class TimestampIncludeRegressionTests
{
    [Fact]
    public async Task ContextMethodIncludeReceivesTimestampArgument()
    {
        var instant = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var include = new EventProjectionInclude(
            EventProjectionContextIntrinsics.Method(nameof(TimeContext.EchoTicks), ""),
            "ticks",
            new EventProjectionArgument("instant", FilterValue.From(instant)));
        var projection = EventProjectionExpression.Default.WithIncludes([include]);
        var compiled = ProjectionCompiler.Compile<TimeContext>(
            typeof(TimeEvent),
            projection,
            ProjectionContextIncludeCompiler.Compile<TimeContext>,
            ProjectionCompilerOptions.Immediate);

        ProjectedEvent projected = await compiled.ProjectAsync(
            new TimeEvent(1),
            new TimeContext(),
            CancellationToken.None);

        Assert.Equal(instant.UtcTicks, projected.ContextValue("ticks").Integer);
    }

    [Fact]
    public async Task IncludeTimestampArgumentsParticipateInPipelineCacheIdentity()
    {
        var firstInstant = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var secondInstant = firstInstant.AddHours(1);

        CompiledEventPipeline<object> first = CompilePipeline(firstInstant);
        CompiledEventPipeline<object> second = CompilePipeline(secondInstant);

        ProjectedEvent? firstProjected = await first.ProjectAsync(
            new TimeEvent(1),
            new object(),
            CancellationToken.None);
        ProjectedEvent? secondProjected = await second.ProjectAsync(
            new TimeEvent(1),
            new object(),
            CancellationToken.None);

        Assert.NotNull(firstProjected);
        Assert.NotNull(secondProjected);
        Assert.Equal(firstInstant.UtcTicks, firstProjected!.ContextValue("cutoff").Integer);
        Assert.Equal(secondInstant.UtcTicks, secondProjected!.ContextValue("cutoff").Integer);
    }

    private static CompiledEventPipeline<object> CompilePipeline(DateTimeOffset instant)
    {
        var include = new EventProjectionInclude(
            "test.timestamp",
            "cutoff",
            new EventProjectionArgument("instant", FilterValue.From(instant)));
        var pipeline = EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Default.WithIncludes([include]));

        return EventPipelineCompiler.Compile<object>(
            typeof(TimeEvent),
            pipeline,
            CompileTimestampInclude,
            EventPipelineCompilerOptions.Immediate);
    }

    private static CompiledProjection<object>.IncludeProjector CompileTimestampInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        DateTimeOffset instant = include.Arguments.Single().Value.Timestamp;
        ProjectedEventValue projected = ProjectedEventValue.FromScalar(instant.UtcTicks);
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => new ValueTask<ProjectedEventValue>(projected));
    }

    private sealed record TimeEvent(long Id) : IFilterSubject;

    private sealed class TimeContext
    {
        public long EchoTicks(DateTimeOffset instant) => instant.UtcTicks;
    }
}

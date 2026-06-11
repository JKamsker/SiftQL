using System.Globalization;
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

    [Fact]
    public async Task IncludeTimestampOffsetsParticipateInPipelineCacheIdentity()
    {
        var utc = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
        var offset = new DateTimeOffset(2026, 2, 3, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(utc.UtcTicks, offset.UtcTicks);
        CompiledEventPipeline<object> first = CompileOffsetPipeline(utc);
        CompiledEventPipeline<object> second = CompileOffsetPipeline(offset);

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
        Assert.Equal(TimeSpan.Zero.Ticks, firstProjected!.ContextValue("offset").Integer);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, secondProjected!.ContextValue("offset").Integer);
    }

    [Fact]
    public async Task ContextMethodIncludeAcceptsTimestampSourceFieldForDateOnlyParameter()
    {
        var instant = new DateTimeOffset(2026, 2, 3, 18, 30, 0, TimeSpan.Zero);
        var include = new EventProjectionInclude(
            EventProjectionContextIntrinsics.Method(nameof(SourceTimestampContext.Format), ""),
            "date",
            EventProjectionArgument.FromSourceField("instant", nameof(TimestampSourceEvent.Instant)));
        var projection = EventProjectionExpression.Default.WithIncludes([include]);
        var compiled = ProjectionCompiler.CompileWithSchema<SourceTimestampContext>(
            typeof(TimestampSourceEvent),
            projection,
            ProjectionContextIncludeCompiler.Compile<SourceTimestampContext>,
            ProjectionCompilerOptions.Immediate,
            errorFactory: null,
            _ => TimestampSourceSchema());

        ProjectedEvent projected = await compiled.ProjectAsync(
            new TimestampSourceEvent(instant),
            new SourceTimestampContext(),
            CancellationToken.None);

        Assert.Equal("2026-02-03", projected.ContextValue("date").String);
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

    private static CompiledEventPipeline<object> CompileOffsetPipeline(DateTimeOffset instant)
    {
        var include = new EventProjectionInclude(
            "test.timestamp.offset",
            "offset",
            new EventProjectionArgument("instant", FilterValue.From(instant)));
        var pipeline = EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Default.WithIncludes([include]));

        return EventPipelineCompiler.Compile<object>(
            typeof(TimeEvent),
            pipeline,
            CompileTimestampOffsetInclude,
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

    private static CompiledProjection<object>.IncludeProjector CompileTimestampOffsetInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        DateTimeOffset instant = include.Arguments.Single().Value.Timestamp;
        ProjectedEventValue projected = ProjectedEventValue.FromScalar(instant.Offset.Ticks);
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => new ValueTask<ProjectedEventValue>(projected));
    }

    private static FilterSchema TimestampSourceSchema() =>
        new(
            typeof(TimestampSourceEvent),
            [
                new FilterField(
                    nameof(TimestampSourceEvent.Instant),
                    typeof(DateTimeOffset),
                    FilterFieldKind.Scalar,
                    static item => ((TimestampSourceEvent)item).Instant,
                    IsCollectionDerived: true),
            ]);

    private sealed record TimeEvent(long Id) : IFilterSubject;

    private sealed record TimestampSourceEvent(DateTimeOffset Instant) : IFilterSubject;

    private sealed class TimeContext
    {
        public long EchoTicks(DateTimeOffset instant) => instant.UtcTicks;
    }

    private sealed class SourceTimestampContext
    {
        public string Format(DateOnly instant) =>
            instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public string Format(string instant) => instant;
    }
}

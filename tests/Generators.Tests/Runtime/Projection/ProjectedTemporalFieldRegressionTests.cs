using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedTemporalFieldRegressionTests
{
    [Fact]
    public async Task ProjectedTimestampFilterMatchesProjectedTemporalField()
    {
        var cutoff = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(TemporalEvent.Instant)))
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field(nameof(TemporalEvent.Instant)),
                FilterOperator.GreaterThan,
                FilterValue.From(cutoff)));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(TemporalEvent),
            pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new TemporalEvent(cutoff.AddMinutes(1)),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            new TemporalEvent(cutoff.AddMinutes(-1)),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task ProjectedTimestampLambdaFilterMatchesProjectedTemporalField()
    {
        var cutoff = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
        QueryKernel<TemporalEvent> query = QueryKernel
            .For<TemporalEvent>()
            .Select(nameof(TemporalEvent.Instant))
            .WhereProjected(ev => ev.Field(nameof(TemporalEvent.Instant)).Timestamp > cutoff);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(TemporalEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new TemporalEvent(cutoff.AddMinutes(1)),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            new TemporalEvent(cutoff.AddMinutes(-1)),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
    }

    private sealed record TemporalEvent(DateTimeOffset Instant) : IFilterSubject;
}

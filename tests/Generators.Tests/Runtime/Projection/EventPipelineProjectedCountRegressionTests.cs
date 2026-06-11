using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineProjectedCountRegressionTests
{
    [Fact]
    public async Task ProjectedArrayCountFilterCanRunAfterProjection()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(TokenEvent.Tokens)))
            .AppendFilter(FilterExpression.Count(
                ProjectedEventPaths.Field(nameof(TokenEvent.Tokens)),
                FilterOperator.GreaterThanOrEqual,
                FilterValue.From(2L)))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Field(nameof(TokenEvent.Tokens)), "Tokens"),
            ]));

        foreach (EventPipelineCompilerOptions options in PipelineOptions())
        {
            CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
                typeof(TokenEvent),
                pipeline,
                ProjectionRuntimeTestSupport.RejectInclude,
                options);

            ProjectedEvent? accepted = await compiled.ProjectAsync(
                new TokenEvent([1, 2]),
                new object(),
                CancellationToken.None);
            ProjectedEvent? rejected = await compiled.ProjectAsync(
                new TokenEvent([1]),
                new object(),
                CancellationToken.None);

            Assert.NotNull(accepted);
            Assert.Null(rejected);
            Assert.Equal(2, accepted!.Field("Tokens").Values.Length);
        }
    }

    private static EventPipelineCompilerOptions[] PipelineOptions() =>
        [EventPipelineCompilerOptions.Immediate, EventPipelineCompilerOptions.Tiered];

    private sealed record TokenEvent(int[] Tokens) : IFilterSubject;
}

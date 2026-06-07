using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class EventPipelineRegressionTests
{
    public static void RunAll()
    {
        ProjectedArrayFilterCanFeedLaterProjection().GetAwaiter().GetResult();
    }

    private static async Task ProjectedArrayFilterCanFeedLaterProjection()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(TokenEvent.Tokens)))
            .AppendFilter(FilterExpression.Contains(
                ProjectedEventPaths.Field(nameof(TokenEvent.Tokens)),
                FilterValue.From(2L)))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
                [new EventProjectionField(ProjectedEventPaths.Field(nameof(TokenEvent.Tokens)), "Tokens")]));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(TokenEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new TokenEvent(Guid.NewGuid(), [1, 2, 3]),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        ProjectedEventValue tokens = projected!.Field(nameof(TokenEvent.Tokens));
        Assert.Equal(ProjectedEventValueKind.Array, tokens.Kind);
        Assert.Contains(tokens.Values, static value => value.Integer == 2);
    }

    private sealed record TokenEvent(Guid EventId, int[] Tokens) : IFilterSubject;
}

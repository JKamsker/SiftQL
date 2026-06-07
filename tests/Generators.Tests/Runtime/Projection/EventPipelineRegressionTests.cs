using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class EventPipelineRegressionTests
{
    public static void RunAll()
    {
        ProjectedArrayFilterCanFeedLaterProjection().GetAwaiter().GetResult();
        ProjectedContextFilterCanRunAfterContextProjection().GetAwaiter().GetResult();
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
        foreach (EventPipelineCompilerOptions options in PipelineOptions())
        {
            CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
                typeof(TokenEvent),
                pipeline,
                ProjectionRuntimeTestSupport.RejectInclude,
                options);

            ProjectedEvent? projected = await compiled.ProjectAsync(
                new TokenEvent(Guid.NewGuid(), [1, 2, 3]),
                new object(),
                CancellationToken.None);
            ProjectedEvent? missed = await compiled.ProjectAsync(
                new TokenEvent(Guid.NewGuid(), [3, 4]),
                new object(),
                CancellationToken.None);

            Assert.NotNull(projected);
            Assert.Null(missed);
            ProjectedEventValue tokens = projected!.Field(nameof(TokenEvent.Tokens));
            Assert.Equal(ProjectedEventValueKind.Array, tokens.Kind);
            Assert.Contains(tokens.Values, static value => value.Integer == 2);
        }
    }

    private static async Task ProjectedContextFilterCanRunAfterContextProjection()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.context", "tag")]))
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Context("tag"),
                FilterOperator.Equal,
                FilterValue.From("ok")))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
                [new EventProjectionField(ProjectedEventPaths.Context("tag"), "tag")]));

        foreach (EventPipelineCompilerOptions options in PipelineOptions())
        {
            CompiledEventPipeline<string> compiled = EventPipelineCompiler.Compile<string>(
                typeof(ItemUsedEvent),
                pipeline,
                CompileStringInclude,
                options);
            ProjectedEvent? rejected = await compiled.ProjectAsync(
                new ItemUsedEvent(Guid.NewGuid(), 1, 100, 1),
                "no",
                CancellationToken.None);
            ProjectedEvent? accepted = await compiled.ProjectAsync(
                new ItemUsedEvent(Guid.NewGuid(), 1, 100, 1),
                "ok",
                CancellationToken.None);

            Assert.Null(rejected);
            Assert.NotNull(accepted);
            Assert.Equal("ok", accepted!.Field("tag").String);
        }
    }

    private static CompiledProjection<string>.IncludeProjector CompileStringInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<string>.IncludeProjector(
            include.ResultName,
            static (_, context, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(context)));
    }

    private static EventPipelineCompilerOptions[] PipelineOptions() =>
        [EventPipelineCompilerOptions.Immediate, EventPipelineCompilerOptions.Tiered];

    private sealed record TokenEvent(Guid EventId, int[] Tokens) : IFilterSubject;
}

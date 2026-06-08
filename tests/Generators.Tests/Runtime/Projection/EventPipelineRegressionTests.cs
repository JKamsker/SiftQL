using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineRegressionTests
{
    [Fact]
    public async Task ProjectedArrayFilterCanFeedLaterProjection()
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

    [Fact]
    public async Task ProjectedContextFilterCanRunAfterContextProjection()
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

    [Fact]
    public async Task DirectProjectedEventProjectionSupportsDynamicFieldPath()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventProjectionExpression.Default.WithFields(
                [new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId")]),
            ProjectionRuntimeTestSupport.RejectInclude);

        ProjectedEvent projected = await projection.ProjectAsync(
            ProjectedEventWithField("ItemId", ProjectedEventValue.FromScalar(100L)),
            new object(),
            CancellationToken.None);

        Assert.Equal(100, projected.Field("ItemId").Integer);
    }

    [Fact]
    public async Task DirectProjectedEventProjectionPreservesInputMetadata()
    {
        var expression = EventProjectionExpression.Default.WithFields(
            [new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId")]);
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            expression,
            ProjectionRuntimeTestSupport.RejectInclude);
        ProjectedEvent source = ProjectedEventWithField(
            "Game.ItemUsedEvent",
            "ItemUsedEvent",
            "ItemId",
            ProjectedEventValue.FromScalar(100L));
        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        ProjectedEvent materialized = await projection.ProjectAsync(source, new object(), CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            source,
            new object(),
            options,
            CancellationToken.None);
        ProjectedEvent roundTripped = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);

        Assert.Equal(source.EventType, materialized.EventType);
        Assert.Equal(source.EventName, materialized.EventName);
        Assert.Equal(source.EventType, roundTripped.EventType);
        Assert.Equal(source.EventName, roundTripped.EventName);
    }

    [Fact]
    public void DirectProjectedEventProjectionRejectsNullFieldsWithValidationException()
    {
        var projection = EventProjectionExpression.Default with
        {
            Fields = [null!],
        };

        Assert.Throws<FilterValidationException>(() =>
            ProjectionCompiler.Compile<object>(
                typeof(ProjectedEvent),
                projection,
                ProjectionRuntimeTestSupport.RejectInclude));
    }

    [Fact]
    public async Task ProjectedEventPipelineStartsWithProjectedSchema()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field("ItemId"),
                FilterOperator.Equal,
                FilterValue.From(100L)))
            .AppendProjection(EventProjectionExpression.Default.WithFields(
                [new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId")]));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            ProjectedEventWithField("ItemId", ProjectedEventValue.FromScalar(100L)),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            ProjectedEventWithField("ItemId", ProjectedEventValue.FromScalar(101L)),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Equal(100, accepted!.Field("ItemId").Integer);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task ProjectedObjectExistsMatchesPresentObject()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Exists(ProjectedEventPaths.Field("Player")));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        ProjectedEvent present = ProjectedEventWithField(
            "Player",
            ProjectedEventValue.FromFields(
            [
                new ProjectedEventField("Id", ProjectedEventValue.FromScalar(7L)),
            ]));
        ProjectedEvent missing = new() { EventType = "Projected", EventName = "Projected" };

        ProjectedEvent? accepted = await compiled.ProjectAsync(present, new object(), CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(missing, new object(), CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
    }

    [Fact]
    public void ProjectionIncludeWithNullArgumentsThrowsValidationException()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude
                {
                    Intrinsic = "test.context",
                    ResultName = "context",
                    Arguments = null!,
                },
            ]));

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                ProjectionRuntimeTestSupport.RejectInclude,
                EventPipelineCompilerOptions.Immediate));
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

    private static ProjectedEvent ProjectedEventWithField(string name, ProjectedEventValue value) =>
        ProjectedEventWithField("Projected", "Projected", name, value);

    private static ProjectedEvent ProjectedEventWithField(
        string eventType,
        string eventName,
        string name,
        ProjectedEventValue value) =>
        new()
        {
            EventType = eventType,
            EventName = eventName,
            Fields = [new ProjectedEventField(name, value)],
        };
}

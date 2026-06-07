using MessagePack;
using MessagePack.Resolvers;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CompiledEventPipelineTests
{
    private static MessagePackSerializerOptions PayloadOptions { get; } =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private sealed record PipeSubject(int Id = 0, string Name = "", bool Active = false) : IFilterSubject;

    private sealed record SubjectA(
        int Id = 0,
        string Region = "",
        bool Flag = false,
        long LargeId = 0,
        double Score = 0.0,
        float FloatScore = 0f,
        decimal Price = 0m,
        ulong ULargeId = 0,
        byte ByteVal = 0,
        Guid Token = default,
        SubjectStatus Status = SubjectStatus.None) : IFilterSubject;

    public enum SubjectStatus { None = 0, Active = 1, Suspended = 2 }

    private static CompiledEventPipeline<object> CompilePipeline(
        Type subjectType,
        EventPipelineExpression? pipeline = null) =>
        EventPipelineCompiler.Compile<object>(
            subjectType, pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_NoFilter_ProducesPayload()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        ReadOnlyMemory<byte>? payload = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 5, Name: "test"), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(payload);
        Assert.NotNull(MessagePackSerializer.Deserialize<ProjectedEvent>(payload!.Value, PayloadOptions));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_FilterRejects_ReturnsNull()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Id), FilterOperator.Equal, FilterValue.From(1L)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var matched = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 1), new object(), PayloadOptions, CancellationToken.None);
        var rejected = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 2), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(matched);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task Pipeline_ProjectAsync_NullSubject_Throws()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await pipeline.ProjectAsync(null!, new object(), CancellationToken.None));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_NullSubject_Throws()
    {
        var pipeline = CompilePipeline(typeof(PipeSubject));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await pipeline.ProjectPayloadAsync(null!, new object(), PayloadOptions, CancellationToken.None));
    }

    [Fact]
    public async Task Pipeline_ProjectPayloadAsync_FieldSelection_ProducesExpectedFields()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(PipeSubject.Name)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var payload = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Id: 99, Name: "Alice", Active: true),
            new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(payload);
        var ev = MessagePackSerializer.Deserialize<ProjectedEvent>(payload!.Value, PayloadOptions);
        Assert.True(ev.TryGetField(nameof(PipeSubject.Name), out var nameField));
        Assert.Equal("Alice", nameField.String);
    }

    [Fact]
    public void Pipeline_Key_ContainsSubjectTypeToken()
        => Assert.Contains("subject:", CompilePipeline(typeof(PipeSubject)).Key);

    [Fact]
    public async Task Pipeline_FilterThenSelect_MatchingSubject_ProducesPayload()
    {
        var pipeExpr = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Active), FilterOperator.Equal, FilterValue.From(true)))
            .AppendProjection(EventProjectionExpression.Select(nameof(PipeSubject.Name)));
        var pipeline = CompilePipeline(typeof(PipeSubject), pipeExpr);
        var active = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Name: "Bob", Active: true), new object(), PayloadOptions, CancellationToken.None);
        var inactive = await pipeline.ProjectPayloadAsync(
            new PipeSubject(Name: "Eve", Active: false), new object(), PayloadOptions, CancellationToken.None);
        Assert.NotNull(active);
        Assert.Null(inactive);
    }

    [Fact]
    public void EventPipelineCompiler_SameArgs_ReturnsCachedInstance()
    {
        var first = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        var second = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        Assert.Same(first, second);
    }

    [Fact]
    public void EventPipelineCompiler_DifferentSubjectTypes_ReturnDifferentInstances()
    {
        var forPipe = EventPipelineCompiler.Compile<object>(
            typeof(PipeSubject), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        var forA = EventPipelineCompiler.Compile<object>(
            typeof(SubjectA), null, ProjectionRuntimeTestSupport.RejectInclude, EventPipelineCompilerOptions.Immediate);
        Assert.NotSame(forPipe, forA);
    }

    [Fact]
    public void EventPipelineCompiler_SourceFilter_NullPipeline_ReturnsAnyExpression()
        => Assert.Equal(FilterExpressionKind.Any, EventPipelineCompiler.SourceFilter(null).Kind);

    [Fact]
    public void EventPipelineCompiler_SourceFilter_PipelineWithPreFilter_ReturnsNonAnyFilter()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Id), FilterOperator.Equal, FilterValue.From(1L)))
            .AppendProjection(EventProjectionExpression.Default);
        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(pipeline);
        Assert.NotEqual(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_NullPipeline_ReturnsProjectedPipeline()
    {
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(null);
        Assert.NotNull(dispatched);
        Assert.True(dispatched.HasProjection);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_PreFilterStripped()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare(nameof(PipeSubject.Active), FilterOperator.Equal, FilterValue.From(true)))
            .AppendProjection(EventProjectionExpression.Default);
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        Assert.True(dispatched.Stages.Length < pipeline.Stages.Length);
    }

    [Fact]
    public void EventPipelineCompiler_ProjectionDispatchPipeline_NoPreFilter_SameStageCount()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default);
        var dispatched = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);
        Assert.Equal(pipeline.Stages.Length, dispatched.Stages.Length);
    }
}

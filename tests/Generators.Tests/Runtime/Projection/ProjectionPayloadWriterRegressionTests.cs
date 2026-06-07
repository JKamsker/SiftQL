using System.Buffers;
using System.Reflection;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionPayloadWriterRegressionTests
{
    [Fact]
    public async Task ProjectPayloadAsyncRoundTripsNoIncludeProjection()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
            ProjectionRuntimeTestSupport.RejectInclude);
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        ProjectedEvent projected = await projection.ProjectAsync(ev, new object(), CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            ev,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ProjectedEvent roundTripped = ProjectionRuntimeTestSupport.Deserialize(payload);

        ProjectionRuntimeTestSupport.AssertEquivalent(projected, roundTripped);
    }

    [Fact]
    public async Task ProjectPayloadAsyncUsesDirectWriterForCompiledFieldArray()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
            ProjectionRuntimeTestSupport.RejectInclude);
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);
        MessagePackSerializerOptions options =
            ProjectionRuntimeTestSupport.PayloadOptions.WithResolver(CompositeResolver.Create(
                [ThrowingProjectedEventFormatter.Instance],
                [ProjectionRuntimeTestSupport.PayloadOptions.Resolver]));

        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            ev,
            new object(),
            options,
            CancellationToken.None);

        ProjectedEvent roundTripped = ProjectionRuntimeTestSupport.Deserialize(payload);
        Assert.Equal(2, roundTripped.Fields.Length);
        Assert.True(roundTripped.TryGetField(nameof(ItemUsedEvent.ItemId), out var itemId));
        Assert.Equal(100, itemId.Integer);
    }

    [Fact]
    public async Task ProjectPayloadAsyncReturnsIndependentBuffersWhenWriterIsReused()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)),
            ProjectionRuntimeTestSupport.RejectInclude);

        ReadOnlyMemory<byte> firstPayload = await projection.ProjectPayloadAsync(
            new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2),
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ReadOnlyMemory<byte> secondPayload = await projection.ProjectPayloadAsync(
            new ItemUsedEvent(Guid.NewGuid(), 10, 200, 2),
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);

        ProjectedEvent first = ProjectionRuntimeTestSupport.Deserialize(firstPayload);
        ProjectedEvent second = ProjectionRuntimeTestSupport.Deserialize(secondPayload);

        Assert.Equal(100, first.Field(nameof(ItemUsedEvent.ItemId)).Integer);
        Assert.Equal(200, second.Field(nameof(ItemUsedEvent.ItemId)).Integer);
    }

    [Fact]
    public void ProjectPayloadAsyncReusesThreadLocalWriterBuffer()
    {
        ClearWriterBuffer();
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)),
            ProjectionRuntimeTestSupport.RejectInclude);

        _ = ProjectPayloadSync(projection, new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2));
        ArrayBufferWriter<byte> first = CurrentWriterBuffer();
        _ = ProjectPayloadSync(projection, new ItemUsedEvent(Guid.NewGuid(), 10, 200, 2));
        ArrayBufferWriter<byte> second = CurrentWriterBuffer();

        Assert.Same(first, second);
    }

    [Fact]
    public void ProjectPayloadAsyncReallocatesWhenBufferTooSmall()
    {
        ClearWriterBuffer();
        var smallProjection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)),
            ProjectionRuntimeTestSupport.RejectInclude);
        var largeProjection = ProjectionCompiler.Compile<object>(
            typeof(LargePayloadSubject),
            EventProjectionExpression.Select(nameof(LargePayloadSubject.Description)),
            ProjectionRuntimeTestSupport.RejectInclude);

        _ = ProjectPayloadSync(smallProjection, new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2));
        ArrayBufferWriter<byte> smallBuffer = CurrentWriterBuffer();
        int smallCapacity = smallBuffer.Capacity;

        _ = ProjectPayloadSync(
            largeProjection,
            new LargePayloadSubject(new string('x', smallCapacity + 1024)));
        ArrayBufferWriter<byte> afterLarge = CurrentWriterBuffer();

        Assert.NotSame(smallBuffer, afterLarge);
        Assert.True(afterLarge.Capacity > smallCapacity);
    }

    [Fact]
    public void ProjectPayloadAsyncDoesNotRetainOversizedWriterBuffer()
    {
        ClearWriterBuffer();
        var projection = ProjectionCompiler.Compile<object>(
            typeof(LargePayloadSubject),
            EventProjectionExpression.Select(nameof(LargePayloadSubject.Description)),
            ProjectionRuntimeTestSupport.RejectInclude);

        _ = ProjectPayloadSync(
            projection,
            new LargePayloadSubject(new string('x', 70 * 1024)));

        Assert.Null(CurrentWriterBufferOrNull());
    }

    [Fact]
    public async Task ProjectPayloadAsyncUsesThreadStaticWriterBuffer()
    {
        ClearWriterBuffer();
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)),
            ProjectionRuntimeTestSupport.RejectInclude);

        ArrayBufferWriter<byte> workerBuffer = await Task.Run(() =>
        {
            ClearWriterBuffer();
            _ = ProjectPayloadSync(projection, new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2));
            return CurrentWriterBuffer();
        });

        Assert.Null(CurrentWriterBufferOrNull());
        _ = ProjectPayloadSync(projection, new ItemUsedEvent(Guid.NewGuid(), 10, 200, 2));

        Assert.NotSame(workerBuffer, CurrentWriterBuffer());
    }

    [Fact]
    public async Task ProjectPayloadAsyncRoundTripsSynchronousIncludeProjection()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression
                .Select(nameof(ItemUsedEvent.ItemId))
                .WithIncludes([new EventProjectionInclude("test.context", "contextValue")]),
            ProjectionRuntimeTestSupport.CompileInclude);
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        ProjectedEvent projected = await projection.ProjectAsync(ev, new object(), CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            ev,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ProjectedEvent roundTripped = ProjectionRuntimeTestSupport.Deserialize(payload);

        ProjectionRuntimeTestSupport.AssertEquivalent(projected, roundTripped);
    }

    private sealed class ThrowingProjectedEventFormatter : IMessagePackFormatter<ProjectedEvent>
    {
        public static readonly ThrowingProjectedEventFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ProjectedEvent value,
            MessagePackSerializerOptions options) =>
            throw new InvalidOperationException("ProjectedEvent DTO serialization should not be used.");

        public ProjectedEvent Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options) =>
            throw new InvalidOperationException("ProjectedEvent DTO deserialization should not be used.");
    }

    private static ReadOnlyMemory<byte> ProjectPayloadSync<TSubject>(
        CompiledProjection<object> projection,
        TSubject subject) =>
        projection.ProjectPayloadAsync(
            subject!,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static ArrayBufferWriter<byte> CurrentWriterBuffer() =>
        CurrentWriterBufferOrNull() ??
        throw new InvalidOperationException("Expected a retained projected payload writer buffer.");

    private static ArrayBufferWriter<byte>? CurrentWriterBufferOrNull() =>
        (ArrayBufferWriter<byte>?)WriterBufferField.GetValue(null);

    private static void ClearWriterBuffer() =>
        WriterBufferField.SetValue(null, null);

    private static FieldInfo WriterBufferField { get; } =
        typeof(ProjectedPayloadWriter).GetField(
            "t_buffer",
            BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new MissingFieldException(typeof(ProjectedPayloadWriter).FullName, "t_buffer");

    private sealed record LargePayloadSubject(string Description) : IFilterSubject;
}

using MessagePack;
using MessagePack.Resolvers;
using SiftQL;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CompiledProjectionAsyncTests
{
    [Fact]
    public async Task AsyncInclude_TriggersAwaitIncludesAsync()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "async-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "async-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("resolved"));
        ProjectedEvent result = await task;

        Assert.Equal(42, result.Field("ItemId").Integer);
        Assert.Equal("resolved", result.ContextValue("async-include").String);
    }

    [Fact]
    public async Task AsyncInclude_TriggersAwaitPayloadIncludesAsync()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "async-payload-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "async-payload-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ValueTask<ReadOnlyMemory<byte>> task = projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("payload-resolved"));
        ReadOnlyMemory<byte> payload = await task;

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(42, deserialized.Field("ItemId").Integer);
        Assert.Equal("payload-resolved", deserialized.ContextValue("async-payload-include").String);
    }

    [Fact]
    public async Task MultipleAsyncIncludes_AllAwaitedInSequence()
    {
        var tcs1 = new TaskCompletionSource<ProjectedEventValue>();
        var tcs2 = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "multi-async-test",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "first",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs1.Task)),
                new CompiledProjection<object>.IncludeProjector(
                    "second",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs2.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs1.SetResult(ProjectedEventValue.FromScalar("a"));
        tcs2.SetResult(ProjectedEventValue.FromScalar("b"));
        ProjectedEvent result = await task;

        Assert.Equal("a", result.ContextValue("first").String);
        Assert.Equal("b", result.ContextValue("second").String);
    }

    [Fact]
    public async Task SyncFirstInclude_AsyncSecondInclude_StillTriggersAwait()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "mixed-sync-async",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "sync-include",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("sync"))),
                new CompiledProjection<object>.IncludeProjector(
                    "async-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("async"));
        ProjectedEvent result = await task;

        Assert.Equal("sync", result.ContextValue("sync-include").String);
        Assert.Equal("async", result.ContextValue("async-include").String);
    }

    [Fact]
    public async Task AllSyncIncludes_CompletedSynchronously()
    {
        var projection = new CompiledProjection<object>(
            "all-sync",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "sync-a",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("a"))),
                new CompiledProjection<object>.IncludeProjector(
                    "sync-b",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("b"))),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.True(task.IsCompletedSuccessfully);

        ProjectedEvent result = await task;
        Assert.Equal("a", result.ContextValue("sync-a").String);
        Assert.Equal("b", result.ContextValue("sync-b").String);
    }

    [Fact]
    public async Task AsyncInclude_AwaitValueAsync_ExercisedViaTaskDelay()
    {
        var projection = new CompiledProjection<object>(
            "await-value-test",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "delayed",
                    async (_, _, _) =>
                    {
                        await Task.Yield();
                        return ProjectedEventValue.FromScalar(999);
                    }),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.Equal(999, result.ContextValue("delayed").Integer);
    }

    [Fact]
    public async Task AsyncPayloadInclude_MultipleAsyncIncludes()
    {
        var tcs1 = new TaskCompletionSource<ProjectedEventValue>();
        var tcs2 = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "multi-async-payload",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "Quantity", "Quantity",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).Quantity))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "inc-a",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs1.Task)),
                new CompiledProjection<object>.IncludeProjector(
                    "inc-b",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs2.Task)),
            ]);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 10);
        ValueTask<ReadOnlyMemory<byte>> task = projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs1.SetResult(ProjectedEventValue.FromScalar("x"));
        tcs2.SetResult(ProjectedEventValue.FromScalar("y"));
        ReadOnlyMemory<byte> payload = await task;

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(10, deserialized.Field("Quantity").Integer);
        Assert.Equal("x", deserialized.ContextValue("inc-a").String);
        Assert.Equal("y", deserialized.ContextValue("inc-b").String);
    }

    [Fact]
    public void CompiledProjection_ThrowsOnNullKey()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            null!, typeof(ItemUsedEvent), fields: [], includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnEmptyKey()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "", typeof(ItemUsedEvent), fields: [], includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnFieldWithNullName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                null!, "Path", _ => ProjectedEventValue.FromScalar(1))],
            includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnFieldWithNullPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "Name", null!, _ => ProjectedEventValue.FromScalar(1))],
            includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnIncludeWithNullName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [],
            includes: [new CompiledProjection<object>.IncludeProjector(
                null!, (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(1)))]));
    }

    [Fact]
    public void CompiledProjection_IsTiered_DefaultFalse()
    {
        var projection = new CompiledProjection<object>(
            "test", typeof(ItemUsedEvent), fields: [], includes: []);
        Assert.False(projection.IsTiered);
        Assert.Null(projection.TieredSnapshot);
    }

    [Fact]
    public async Task CompiledProjection_NoFieldsNoIncludes_ProjectsEmptyEvent()
    {
        var projection = new CompiledProjection<object>(
            "empty", typeof(ItemUsedEvent), fields: [], includes: []);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.Equal(typeof(ItemUsedEvent).FullName, result.EventType);
        Assert.Equal(nameof(ItemUsedEvent), result.EventName);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void CompiledProjection_FieldProjectorProject_ReturnsCorrectField()
    {
        var projector = new CompiledProjection<object>.FieldProjector(
            "TestField", "TestPath",
            _ => ProjectedEventValue.FromScalar(42));

        ProjectedEventField field = projector.Project(new object());
        Assert.Equal("TestField", field.Name);
        Assert.Equal(42, field.Value.Integer);
    }

    [Fact]
    public async Task CompiledProjection_PayloadNoIncludes_WritesCorrectly()
    {
        var projection = new CompiledProjection<object>(
            "payload-no-includes",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes: []);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(42, deserialized.Field("ItemId").Integer);
    }

    [Fact]
    public async Task CompiledProjection_PromoteProjectFields_ReplacesFieldProjection()
    {
        var projection = new CompiledProjection<object>(
            "promote-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes: []);

        projection.PromoteProjectFields(static _ =>
            [new ProjectedEventField("Promoted", ProjectedEventValue.FromScalar(999))]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.True(result.TryGetField("Promoted", out var field));
        Assert.Equal(999, field.Integer);
    }
}

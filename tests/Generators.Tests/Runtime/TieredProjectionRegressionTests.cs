using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class TieredProjectionRegressionTests
{
    public static void RunAll()
    {
        TieredProjectionStartsInterpretedAndCountsOperations().GetAwaiter().GetResult();
        TieredProjectionPayloadMatchesImmediatePayload().GetAwaiter().GetResult();
        HotTieredProjectionPromotesOffThread().GetAwaiter().GetResult();
        HotTieredProjectionWithIncludesPromotesFieldArray().GetAwaiter().GetResult();
    }

    private static async Task TieredProjectionStartsInterpretedAndCountsOperations()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
            ProjectionRuntimeTestSupport.RejectInclude,
            ProjectionCompilerOptions.Tiered);
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        Assert.True(projection.IsTiered);
        Assert.Equal(TieredProjectionTier.Interpreted, projection.TieredSnapshot?.Tier);

        await projection.ProjectAsync(ev, new object(), CancellationToken.None);
        TieredProjectionSnapshot materialized = projection.TieredSnapshot!;
        Assert.Equal(1, materialized.Materializations);
        Assert.Equal(0, materialized.PayloadWrites);

        await projection.ProjectPayloadAsync(
            ev,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        TieredProjectionSnapshot payload = projection.TieredSnapshot!;
        Assert.Equal(TieredProjectionTier.Interpreted, payload.Tier);
        Assert.Equal(1, payload.Materializations);
        Assert.Equal(1, payload.PayloadWrites);
        Assert.False(payload.CompilationQueued);
        Assert.False(payload.CompilationFailed);
    }

    private static async Task TieredProjectionPayloadMatchesImmediatePayload()
    {
        EventProjectionExpression expression = EventProjectionExpression
            .Select(nameof(ItemUsedEvent.ItemId))
            .WithIncludes([new EventProjectionInclude("test.context", "contextValue")]);
        var immediate = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            expression,
            ProjectionRuntimeTestSupport.CompileInclude);
        var tiered = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            expression,
            ProjectionRuntimeTestSupport.CompileInclude,
            ProjectionCompilerOptions.Tiered);
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        ReadOnlyMemory<byte> immediatePayload = await immediate.ProjectPayloadAsync(
            ev,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ReadOnlyMemory<byte> tieredPayload = await tiered.ProjectPayloadAsync(
            ev,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);

        Assert.Equal(immediatePayload.ToArray(), tieredPayload.ToArray());
        Assert.Equal(1, tiered.TieredSnapshot?.PayloadWrites);
    }

    private static async Task HotTieredProjectionPromotesOffThread()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
            ProjectionRuntimeTestSupport.RejectInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
            });
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        await projection.ProjectAsync(ev, new object(), CancellationToken.None);

        TieredProjectionSnapshot snapshot = await ProjectionRuntimeTestSupport.WaitForSnapshotAsync(
            projection,
            static item => item.Tier == TieredProjectionTier.Compiled);
        Assert.False(snapshot.CompilationQueued);
        Assert.False(snapshot.CompilationFailed);

        ProjectedEvent projected = await projection.ProjectAsync(ev, new object(), CancellationToken.None);
        Assert.True(projected.TryGetField(nameof(ItemUsedEvent.ItemId), out var itemId));
        Assert.Equal(100, itemId.Integer);
        Assert.Equal(snapshot.Materializations, projection.TieredSnapshot?.Materializations);
    }

    private static async Task HotTieredProjectionWithIncludesPromotesFieldArray()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression
                .Select(nameof(ItemUsedEvent.ItemId))
                .WithIncludes([new EventProjectionInclude("test.context", "contextValue")]),
            ProjectionRuntimeTestSupport.CompileInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
            });
        var ev = new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2);

        ProjectedEvent projected = await projection.ProjectAsync(ev, new object(), CancellationToken.None);

        TieredProjectionSnapshot snapshot = await ProjectionRuntimeTestSupport.WaitForSnapshotAsync(
            projection,
            static item => item.Tier == TieredProjectionTier.Compiled);
        Assert.False(snapshot.CompilationFailed);
        Assert.True(projected.TryGetField(nameof(ItemUsedEvent.ItemId), out _));
        Assert.True(projected.TryGetContext("contextValue", out var context));
        Assert.Equal("included", context.String);
    }
}

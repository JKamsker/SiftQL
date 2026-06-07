using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class TieredProjectionRegressionTests
{
    [Fact]
    public async Task TieredProjectionStartsInterpretedAndCountsOperations()
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

    [Fact]
    public async Task TieredProjectionPayloadMatchesImmediatePayload()
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

    [Fact]
    public async Task HotTieredProjectionPromotesOffThread()
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

    [Fact]
    public async Task HotTieredProjectionWithIncludesPromotesFieldArray()
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

    [Fact]
    public void FailedProjectionRetryDelayMatchesFilterPolicy()
    {
        Type stateType = typeof(ProjectionCompiler).Assembly
            .GetType("SiftQL.Projection.TieredProjectionState`1", throwOnError: true)!
            .MakeGenericType(typeof(object));
        var field = stateType.GetField(
            "s_failedRetryDelay",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Equal(TimeSpan.FromSeconds(30), (TimeSpan)field.GetValue(null)!);
    }

    [Fact]
    public async Task FailedProjectionPromotionRetriesWhenProviderAppears()
    {
        EventProjectionExpression expression = EventProjectionExpression.Select(
            nameof(ProjectionRecoverySubject.Value));
        var schema = new FilterSchema(
            typeof(ProjectionRecoverySubject),
            [
                new FilterField(
                    nameof(ProjectionRecoverySubject.Value),
                    typeof(int),
                    FilterFieldKind.Scalar,
                    static subject => ((ProjectionRecoverySubject)subject).Value,
                    Access: FilterFieldAccess.ForProperty("Missing")),
            ]);
        CompiledProjection<object> projection = ProjectionCompiler.CompileWithSchema<object>(
            typeof(ProjectionRecoverySubject),
            expression,
            ProjectionRuntimeTestSupport.RejectInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
            },
            errorFactory: null,
            _ => schema);
        var subject = new ProjectionRecoverySubject(2);

        ProjectedEvent interpreted = await projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.Equal(2, interpreted.Field(nameof(ProjectionRecoverySubject.Value)).Integer);
        await ProjectionRuntimeTestSupport.WaitForSnapshotAsync(
            projection,
            static item => item.CompilationFailed);

        using var registration = PrecompiledTieredProviderRegistry.Register(new ProjectionRecoveryProvider());
        await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        await ProjectionRuntimeTestSupport.WaitForSnapshotAsync(
            projection,
            static item => item.Tier == TieredProjectionTier.Compiled);
        ProjectedEvent recovered = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.True(recovered.TryGetField("provided", out var provided));
        Assert.Equal(9, provided.Integer);
    }

    private sealed record ProjectionRecoverySubject(int Value);

    private sealed class ProjectionRecoveryProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = null;
            return false;
        }

        public bool TryGetProjection(
            Type subjectType,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = static _ =>
                [new ProjectedEventField("provided", ProjectedEventValue.FromScalar(9))];
            return true;
        }
    }
}

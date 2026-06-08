using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Tiered;

namespace SiftQL.Generators.Tests;

public sealed class TieredProviderVersionRegressionTests
{
    [Fact]
    public async Task KernelPromotionRetriesWhenProviderChangesDuringFailedAttempt()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        IDisposable? registration = null;
        int compileCalls = 0;
        var state = new TieredKernelState(
            interpreted: static _ => true,
            compilePromoted: () =>
            {
                if (Interlocked.Increment(ref compileCalls) == 1)
                {
                    registration = PrecompiledTieredProviderRegistry.Register(new NoopProvider());
                    return null;
                }

                return KernelPredicate.FromObject(static _ => false);
            },
            promotionPolicy: new TieredFilterPromotionPolicy(1, TimeSpan.Zero, 16),
            recordHot: null,
            publishPromoted: static _ => { });

        try
        {
            Assert.True(state.Matches(new object()));
            await WaitForKernelSnapshotAsync(state, static item => item.CompilationFailed);

            Assert.True(state.Matches(new object()));
            await WaitForKernelSnapshotAsync(state, static item => item.Tier == TieredKernelTier.Compiled);

            Assert.Equal(2, Volatile.Read(ref compileCalls));
        }
        finally
        {
            registration?.Dispose();
        }
    }

    [Fact]
    public async Task ProjectionPromotionRetriesWhenProviderChangesDuringFailedAttempt()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        IDisposable? registration = null;
        int compileCalls = 0;
        var state = new TieredProjectionState<object>(
            compileProjectFields: () =>
            {
                if (Interlocked.Increment(ref compileCalls) == 1)
                {
                    registration = PrecompiledTieredProviderRegistry.Register(new NoopProvider());
                    return null;
                }

                return static _ => [new ProjectedEventField("Value", ProjectedEventValue.FromScalar(1))];
            },
            promotionPolicy: new TieredProjectionPromotionPolicy(1, TimeSpan.Zero, 16),
            recordHot: null,
            publishProjectFields: static _ => { });

        try
        {
            state.RecordMaterialization();
            await WaitForProjectionSnapshotAsync(state, static item => item.CompilationFailed);

            state.RecordMaterialization();
            await WaitForProjectionSnapshotAsync(state, static item => item.Tier == TieredProjectionTier.Compiled);

            Assert.Equal(2, Volatile.Read(ref compileCalls));
        }
        finally
        {
            registration?.Dispose();
        }
    }

    private static async Task<TieredKernelSnapshot> WaitForKernelSnapshotAsync(
        TieredKernelState state,
        Func<TieredKernelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            TieredKernelSnapshot snapshot = state.Snapshot;
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered kernel state did not reach expected state. Last snapshot: {state.Snapshot}");
    }

    private static async Task<TieredProjectionSnapshot> WaitForProjectionSnapshotAsync(
        TieredProjectionState<object> state,
        Func<TieredProjectionSnapshot, bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            TieredProjectionSnapshot snapshot = state.Snapshot;
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered projection state did not reach expected state. Last snapshot: {state.Snapshot}");
    }

    private sealed class NoopProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(
            Type subjectType,
            string fingerprint,
            out Func<object, bool>? predicate)
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
            projectFields = null;
            return false;
        }
    }
}

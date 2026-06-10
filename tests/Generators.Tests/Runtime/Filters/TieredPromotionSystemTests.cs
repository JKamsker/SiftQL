using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Tiered;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class TieredPromotionSystemTests
{
    [Fact]
    public void FilterPromotionPolicyUsesConfiguredThresholds()
    {
        TieredFilterPromotionPolicy policy = (FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromMilliseconds(25),
            TieredPromotionMinimumEvaluations = 7,
            TieredPromotionQueueCapacity = 3,
        }).CreateFilterPromotionPolicy(FilterExpression.Any);

        Assert.Equal(7, policy.MinimumEvaluations);
        Assert.Equal(TimeSpan.FromMilliseconds(25), policy.MinimumAge);
        Assert.Equal(3, policy.QueueCapacity);
    }

    [Fact]
    public async Task PromotionQueueRejectsWorkWhenCapacityIsFull()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();

        bool firstQueued = TieredPromotionQueue.TryQueue(
            () =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                completed.Set();
            },
            capacity: 1);

        Assert.True(firstQueued);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(TieredPromotionQueue.TryQueue(static () => { }, capacity: 1));

        release.Set();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        await WaitForQueueDrainAsync();
    }

    [Fact]
    public async Task KernelStateQueuesOnlyAfterMinimumEvaluations()
    {
        int compileCalls = 0;
        int publishCalls = 0;
        var state = new TieredKernelState(
            interpreted: static _ => true,
            compilePromoted: () =>
            {
                Interlocked.Increment(ref compileCalls);
                return KernelPredicate.FromObject(static _ => true);
            },
            promotionPolicy: new TieredFilterPromotionPolicy(2, TimeSpan.Zero, 16),
            recordHot: null,
            publishPromoted: _ => Interlocked.Increment(ref publishCalls));

        Assert.True(state.Matches(new object()));
        Assert.Equal(0, Volatile.Read(ref compileCalls));
        Assert.False(state.Snapshot.CompilationQueued);

        Assert.True(state.Matches(new object()));
        TieredKernelSnapshot snapshot = await WaitForSnapshotAsync(
            state,
            static item => item.Tier == TieredKernelTier.Compiled);

        Assert.Equal(TieredKernelTier.Compiled, snapshot.Tier);
        Assert.Equal(1, Volatile.Read(ref compileCalls));
        Assert.Equal(1, Volatile.Read(ref publishCalls));
    }

    private static async Task<TieredKernelSnapshot> WaitForSnapshotAsync(
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

    private static async Task WaitForQueueDrainAsync()
    {
        for (int i = 0; i < 500; i++)
        {
            if (TieredPromotionQueue.IsIdle)
                return;

            await Task.Delay(10);
        }

        throw new InvalidOperationException("Tiered promotion queue did not drain.");
    }
}

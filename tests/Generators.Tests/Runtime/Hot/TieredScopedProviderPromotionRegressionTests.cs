using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Tiered;

namespace SiftQL.Generators.Tests;

public sealed class TieredScopedProviderPromotionRegressionTests
{
    [Fact]
    public async Task ParameterizedPromotionUsesScopedProviderContext()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L) with { ParameterKey = "p0" });
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
            });
        using var registration = PrecompiledTieredProviderRegistry.Register(
            new ParameterizedRejectingProvider(
                typeof(ItemUsedEvent),
                FilterExpressionFingerprint.Create(filter)));
        var subject = new ItemUsedEvent(Guid.NewGuid(), 7, 100, 1);

        Assert.True(kernel.Matches(subject));
        bool afterPromotion = await WaitForFalseAsync(kernel, subject);

        Assert.False(afterPromotion);
    }

    [Fact]
    public void DisposedIsolatedProviderScopeIsNotVisibleThroughCapturedExecutionContext()
    {
        IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new ParameterizedRejectingProvider(typeof(ItemUsedEvent), "unused"));
        ExecutionContext? captured = ExecutionContext.Capture();
        scope.Dispose();

        try
        {
            bool hasProviders = true;
            ExecutionContext.Run(captured!, _ =>
            {
                hasProviders = PrecompiledTieredProviderRegistry.HasProviders;
            }, null);

            Assert.False(hasProviders);
        }
        finally
        {
            registration.Dispose();
        }
    }

    [Fact]
    public void DisposedNestedProviderScopeFallsBackToActiveParentInCapturedExecutionContext()
    {
        using IDisposable outer = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new ParameterizedRejectingProvider(typeof(ItemUsedEvent), "unused"));
        IDisposable inner = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        ExecutionContext? captured = ExecutionContext.Capture();
        inner.Dispose();

        bool hasProviders = false;
        ExecutionContext.Run(captured!, _ =>
        {
            hasProviders = PrecompiledTieredProviderRegistry.HasProviders;
        }, null);

        Assert.True(hasProviders);
    }

    private static async Task<bool> WaitForFalseAsync(
        CompiledKernel kernel,
        object subject)
    {
        bool matched = true;
        for (int i = 0; i < 500; i++)
        {
            matched = kernel.Matches(subject);
            if (!matched)
                return false;

            await Task.Delay(10);
        }

        return matched;
    }

    private sealed class ParameterizedRejectingProvider(
        Type subjectType,
        string fingerprint) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(
            Type requestedType,
            string requestedFingerprint,
            out Func<object, bool>? predicate)
        {
            _ = requestedType;
            _ = requestedFingerprint;
            predicate = null;
            return false;
        }

        public bool TryGetParameterizedFilter(
            Type requestedType,
            string requestedFingerprint,
            out ParameterizedHotFilterPredicate? predicate)
        {
            predicate = requestedType == subjectType &&
                string.Equals(requestedFingerprint, fingerprint, StringComparison.Ordinal)
                    ? static (_, _) => false
                    : null;
            return predicate is not null;
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

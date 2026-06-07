using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Tiered;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class TieredProviderRecoveryTests
{
    public static void RunAll()
    {
        FailedFilterPromotionRetriesWhenProviderAppears().GetAwaiter().GetResult();
        ParameterizedFilterPromotionCanUseProviderRegisteredAfterCompile()
            .GetAwaiter()
            .GetResult();
    }

    private static async Task FailedFilterPromotionRetriesWhenProviderAppears()
    {
        var filter = FilterExpression.Compare(
            nameof(RecoverySubject.Value),
            FilterOperator.Equal,
            FilterValue.From(2L));
        var schema = new FilterSchema(
            typeof(RecoverySubject),
            [
                new FilterField(
                    nameof(RecoverySubject.Value),
                    typeof(int),
                    FilterFieldKind.Scalar,
                    static subject => ((RecoverySubject)subject).Value,
                    Access: FilterFieldAccess.ForProperty("Missing")),
            ]);
        CompiledKernel kernel = FilterCompiler.CompileWithSchema(
            typeof(RecoverySubject),
            filter,
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
            },
            errorFactory: null,
            _ => schema);
        var subject = new RecoverySubject(2);

        Assert.True(kernel.Matches(subject));
        await WaitForSnapshotAsync(kernel, static snapshot => snapshot.CompilationFailed);

        string fingerprint = FilterExpressionFingerprint.Create(filter);
        using var registration = PrecompiledTieredProviderRegistry.Register(
            new RejectingProvider(typeof(RecoverySubject), fingerprint));

        bool afterProvider = await WaitForFalseAsync(kernel, subject);

        Assert.False(afterProvider);
    }

    private static async Task ParameterizedFilterPromotionCanUseProviderRegisteredAfterCompile()
    {
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
        var subject = new ItemUsedEvent(Guid.NewGuid(), 7, 100, 1);

        using var registration = PrecompiledTieredProviderRegistry.Register(
            new ParameterizedRejectingProvider(typeof(ItemUsedEvent), FilterExpressionFingerprint.Create(filter)));

        Assert.True(kernel.Matches(subject));
        bool afterPromotion = await WaitForFalseAsync(kernel, subject);

        Assert.False(afterPromotion);
    }

    private static async Task<TieredKernelSnapshot> WaitForSnapshotAsync(
        CompiledKernel kernel,
        Func<TieredKernelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            TieredKernelSnapshot? snapshot = kernel.TieredSnapshot;
            if (snapshot is not null && predicate(snapshot))
                return snapshot;
            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered kernel did not reach expected state. Last snapshot: {kernel.TieredSnapshot}");
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

    private sealed record RecoverySubject(int Value);

    private sealed class RejectingProvider(Type subjectType, string fingerprint) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(
            Type requestedType,
            string requestedFingerprint,
            out Func<object, bool>? predicate)
        {
            predicate = requestedType == subjectType &&
                string.Equals(requestedFingerprint, fingerprint, StringComparison.Ordinal)
                ? static _ => false
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

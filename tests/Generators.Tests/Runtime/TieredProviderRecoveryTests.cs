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
        RecoverySubject subject)
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
}

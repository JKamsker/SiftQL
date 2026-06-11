using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Tiered;

namespace SiftQL.Generators.Tests;

public sealed class TieredProviderScopeRetryRegressionTests
{
    [Fact]
    public async Task FailedFilterPromotionRetriesWhenProviderScopeViewChanges()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(ScopeRetrySubject.Value),
            FilterOperator.Equal,
            FilterValue.From(2L));
        FilterSchema schema = MissingAccessSchema();
        ScopeRetrySubject subject = new(2);

        using var outer = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new RejectingFilterProvider(typeof(ScopeRetrySubject), FilterExpressionFingerprint.Create(filter)));
        CompiledKernel kernel;
        using (PrecompiledTieredProviderRegistry.CreateIsolatedScope())
        {
            kernel = FilterCompiler.CompileWithSchema(
                typeof(ScopeRetrySubject),
                filter,
                TieredOptions(),
                errorFactory: null,
                _ => schema);

            Assert.True(kernel.Matches(subject));
            await WaitForSnapshotAsync(kernel, static snapshot => snapshot.CompilationFailed);
        }

        bool afterProvider = await WaitForFalseAsync(kernel, subject);

        Assert.False(afterProvider);
    }

    [Fact]
    public async Task FailedProjectionPromotionRetriesWhenProviderScopeViewChanges()
    {
        EventProjectionExpression expression = EventProjectionExpression.Select(
            nameof(ScopeRetrySubject.Value));
        FilterSchema schema = MissingAccessSchema();
        ScopeRetrySubject subject = new(2);

        using var outer = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new ProvidedProjectionProvider(
                typeof(ScopeRetrySubject),
                ProjectionExpressionFingerprint.Create(expression)));
        CompiledProjection<object> projection;
        using (PrecompiledTieredProviderRegistry.CreateIsolatedScope())
        {
            projection = ProjectionCompiler.CompileWithSchema<object>(
                typeof(ScopeRetrySubject),
                expression,
                ProjectionRuntimeTestSupport.RejectInclude,
                ProjectionCompilerOptions.Tiered with
                {
                    TieredPromotionMinimumAge = TimeSpan.Zero,
                    TieredPromotionMinimumOperations = 1,
                },
                errorFactory: null,
                _ => schema);

            ProjectedEvent interpreted = await projection.ProjectAsync(subject, new object(), CancellationToken.None);
            Assert.Equal(2, interpreted.Field(nameof(ScopeRetrySubject.Value)).Integer);
            await WaitForProjectionSnapshotAsync(projection, static snapshot => snapshot.CompilationFailed);
        }

        await projection.ProjectAsync(subject, new object(), CancellationToken.None);
        await WaitForProjectionSnapshotAsync(projection, static snapshot => snapshot.Tier == TieredProjectionTier.Compiled);
        ProjectedEvent recovered = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.Equal(9, recovered.Field("provided").Integer);
    }

    private static FilterCompilerOptions TieredOptions() =>
        FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.Zero,
            TieredPromotionMinimumEvaluations = 1,
        };

    private static FilterSchema MissingAccessSchema() =>
        new(
            typeof(ScopeRetrySubject),
            [
                new FilterField(
                    nameof(ScopeRetrySubject.Value),
                    typeof(int),
                    FilterFieldKind.Scalar,
                    static subject => ((ScopeRetrySubject)subject).Value,
                    Access: FilterFieldAccess.ForProperty("Missing")),
            ]);

    private static async Task<TieredKernelSnapshot> WaitForSnapshotAsync(
        CompiledKernel kernel,
        Func<TieredKernelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++)
        {
            TieredKernelSnapshot? snapshot = kernel.TieredSnapshot;
            if (snapshot is not null && predicate(snapshot))
                return snapshot;
            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered kernel did not reach expected state. Last snapshot: {kernel.TieredSnapshot}");
    }

    private static async Task<TieredProjectionSnapshot> WaitForProjectionSnapshotAsync(
        CompiledProjection<object> projection,
        Func<TieredProjectionSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++)
        {
            TieredProjectionSnapshot? snapshot = projection.TieredSnapshot;
            if (snapshot is not null && predicate(snapshot))
                return snapshot;
            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered projection did not reach expected state. Last snapshot: {projection.TieredSnapshot}");
    }

    private static async Task<bool> WaitForFalseAsync(CompiledKernel kernel, object subject)
    {
        bool matched = true;
        for (int i = 0; i < 200; i++)
        {
            matched = kernel.Matches(subject);
            if (!matched)
                return false;
            await Task.Delay(10);
        }

        return matched;
    }

    private sealed record ScopeRetrySubject(int Value) : IFilterSubject;

    private sealed class RejectingFilterProvider(Type subjectType, string fingerprint) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type requestedType, string requestedFingerprint, out Func<object, bool>? predicate)
        {
            predicate = requestedType == subjectType &&
                string.Equals(requestedFingerprint, fingerprint, StringComparison.Ordinal)
                ? static _ => false
                : null;
            return predicate is not null;
        }

        public bool TryGetProjection(Type subjectType, string fingerprint, out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = null;
            return false;
        }
    }

    private sealed class ProvidedProjectionProvider(Type subjectType, string fingerprint) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = null;
            return false;
        }

        public bool TryGetProjection(
            Type requestedType,
            string requestedFingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            projectFields = requestedType == subjectType &&
                string.Equals(requestedFingerprint, fingerprint, StringComparison.Ordinal)
                ? static _ => [new ProjectedEventField("provided", ProjectedEventValue.FromScalar(9))]
                : null;
            return projectFields is not null;
        }
    }
}

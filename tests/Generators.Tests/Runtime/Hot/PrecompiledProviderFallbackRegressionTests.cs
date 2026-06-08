using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class PrecompiledProviderFallbackRegressionTests
{
    [Fact]
    public void NullFilterClaimFallsBackToOlderProvider()
    {
        FilterExpression filter = ItemIdFilter(100);
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var valid = PrecompiledTieredProviderRegistry.Register(
            Provider.Filter(typeof(ItemUsedEvent), fingerprint));
        using var nullClaim = PrecompiledTieredProviderRegistry.Register(
            Provider.NullFilter(typeof(ItemUsedEvent), fingerprint));

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.False(kernel.IsTiered);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 100, 1)));
    }

    [Fact]
    public async Task NullProjectionClaimFallsBackToOlderProvider()
    {
        EventProjectionExpression projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        string fingerprint = ProjectionExpressionFingerprint.Create(projection);
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var valid = PrecompiledTieredProviderRegistry.Register(
            Provider.Projection(typeof(ItemUsedEvent), fingerprint));
        using var nullClaim = PrecompiledTieredProviderRegistry.Register(
            Provider.NullProjection(typeof(ItemUsedEvent), fingerprint));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);

        Assert.False(compiled.IsTiered);
        ProjectedEvent projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 1, 100, 1),
            new object(),
            CancellationToken.None);
        Assert.Equal("provided", Assert.Single(projected.Fields).Name);
    }

    private static FilterExpression ItemIdFilter(long itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId));

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed class Provider(
        Type subjectType,
        string fingerprint,
        bool filter,
        bool nullClaim) : IPrecompiledTieredProvider
    {
        public static Provider Filter(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: true, nullClaim: false);

        public static Provider NullFilter(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: true, nullClaim: true);

        public static Provider Projection(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: false, nullClaim: false);

        public static Provider NullProjection(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: false, nullClaim: true);

        public bool TryGetFilter(Type type, string key, out Func<object, bool>? predicate)
        {
            if (!filter || type != subjectType || key != fingerprint)
            {
                predicate = null;
                return false;
            }

            predicate = nullClaim ? null : static _ => true;
            return true;
        }

        public bool TryGetProjection(
            Type type,
            string key,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            if (filter || type != subjectType || key != fingerprint)
            {
                projectFields = null;
                return false;
            }

            projectFields = nullClaim
                ? null
                : static _ => [new ProjectedEventField("provided", ProjectedEventValue.FromScalar(1))];
            return true;
        }
    }
}

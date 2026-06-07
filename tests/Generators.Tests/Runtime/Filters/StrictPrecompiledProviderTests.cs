using MessagePack;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class StrictPrecompiledProviderTests
{
    [Fact]
    public void PrecompiledFilterProviderRequiresMatchingFingerprint()
    {
        FilterExpression expected = ItemIdFilter(100);
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(
            StrictProvider.ForFilter(typeof(ItemUsedEvent), FilterExpressionFingerprint.Create(expected)));

        CompiledKernel matching = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            expected,
            FilterCompilerOptions.Tiered);
        CompiledKernel mismatched = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            ItemIdFilter(200),
            FilterCompilerOptions.Tiered);

        Assert.False(matching.IsTiered);
        Assert.True(mismatched.IsTiered);
    }

    [Fact]
    public void PrecompiledProjectionProviderRequiresMatchingFingerprint()
    {
        EventProjectionExpression expected = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(
            StrictProvider.ForProjection(
                typeof(ItemUsedEvent),
                ProjectionExpressionFingerprint.Create(expected)));

        CompiledProjection<object> matching = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            expected,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);
        CompiledProjection<object> mismatched = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity)),
            RejectInclude,
            ProjectionCompilerOptions.Tiered);

        Assert.False(matching.IsTiered);
        Assert.True(mismatched.IsTiered);
    }

    [Fact]
    public async Task PrecompiledProjectionProviderWritesProvidedPayloadFields()
    {
        EventProjectionExpression expected = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(
            StrictProvider.ForProjection(
                typeof(ItemUsedEvent),
                ProjectionExpressionFingerprint.Create(expected)));
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            expected,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);

        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            new ItemUsedEvent(Guid.NewGuid(), 1, 100, 1),
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ProjectedEvent projected = MessagePackSerializer.Deserialize<ProjectedEvent>(
            payload,
            ProjectionRuntimeTestSupport.PayloadOptions);

        ProjectedEventField field = Assert.Single(projected.Fields);
        Assert.Equal("provided", field.Name);
        Assert.Equal(1, field.Value.Integer);
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

    private sealed class StrictProvider(
        Type subjectType,
        string fingerprint,
        bool filter) : IPrecompiledTieredProvider
    {
        public static StrictProvider ForFilter(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: true);

        public static StrictProvider ForProjection(Type subjectType, string fingerprint) =>
            new(subjectType, fingerprint, filter: false);

        public bool TryGetFilter(Type type, string key, out Func<object, bool>? predicate)
        {
            predicate = filter && type == subjectType && key == fingerprint
                ? static _ => true
                : null;
            return predicate is not null;
        }

        public bool TryGetProjection(
            Type type,
            string key,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            projectFields = !filter && type == subjectType && key == fingerprint
                ? static _ => [new ProjectedEventField("provided", ProjectedEventValue.FromScalar(1))]
                : null;
            return projectFields is not null;
        }
    }
}

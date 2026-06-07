using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterCompilerCacheFindingTests
{
    [Fact]
    public void FilterCacheKeyReflectsMutatedValueArrays()
    {
        var filter = FilterExpression.In(
            nameof(ItemUsedEvent.ItemId),
            [FilterValue.From(100L)]);

        CompiledKernel first = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate);

        filter.Values[0] = FilterValue.From(200L);
        CompiledKernel second = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(first.Matches(Event(itemId: 100)));
        Assert.False(first.Matches(Event(itemId: 200)));
        Assert.False(second.Matches(Event(itemId: 100)));
        Assert.True(second.Matches(Event(itemId: 200)));
    }

    [Fact]
    public void CachedImmediateFilterDoesNotRebuildSchemaOnCacheHit()
    {
        var filter = FilterExpression.Compare(
            nameof(CacheSubject.Flag),
            FilterOperator.Equal,
            FilterValue.From(true));
        FilterSchema schema = CacheSubjectSchema();
        int calls = 0;
        FilterSchema CountedSchema(Type _)
        {
            calls++;
            return schema;
        }

        CompiledKernel first = FilterCompiler.CompileWithSchema(
            typeof(CacheSubject),
            filter,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            CountedSchema);
        CompiledKernel second = FilterCompiler.CompileWithSchema(
            typeof(CacheSubject),
            filter,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            CountedSchema);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PipelineCacheKeyReflectsMutatedProjectionFields()
    {
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression pipeline = EventPipelineExpression.Default.AppendProjection(projection);
        var ev = Event(itemId: 100);

        CompiledEventPipeline<object> first = CompilePipeline(pipeline);
        ProjectedEvent? firstProjected = await first.ProjectAsync(ev, new object(), CancellationToken.None);

        projection.Fields[0] = new EventProjectionField(nameof(ItemUsedEvent.Quantity));
        CompiledEventPipeline<object> second = CompilePipeline(pipeline);
        ProjectedEvent? secondProjected = await second.ProjectAsync(ev, new object(), CancellationToken.None);

        Assert.NotNull(firstProjected);
        Assert.True(firstProjected!.TryGetField(nameof(ItemUsedEvent.ItemId), out _));
        Assert.NotNull(secondProjected);
        Assert.False(secondProjected!.TryGetField(nameof(ItemUsedEvent.ItemId), out _));
        Assert.Equal(2, secondProjected.Field(nameof(ItemUsedEvent.Quantity)).Integer);
    }

    [Fact]
    public void ProviderRegistrationDisposeIsIdempotent()
    {
        IDisposable registration = PrecompiledTieredProviderRegistry.Register(new Provider(static _ => true));
        int afterRegister = PrecompiledTieredProviderRegistry.GlobalVersion;

        registration.Dispose();
        int afterFirstDispose = PrecompiledTieredProviderRegistry.GlobalVersion;
        registration.Dispose();
        int afterSecondDispose = PrecompiledTieredProviderRegistry.GlobalVersion;

        Assert.True(afterFirstDispose > afterRegister);
        Assert.Equal(afterFirstDispose, afterSecondDispose);
    }

    private static FilterSchema CacheSubjectSchema() =>
        new(
            typeof(CacheSubject),
            [
                new FilterField(
                    nameof(CacheSubject.Flag),
                    typeof(bool),
                    FilterFieldKind.Scalar,
                    static subject => ((CacheSubject)subject).Flag,
                    ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(
                        ((CacheSubject)subject).Flag)),
            ]);

    private static ItemUsedEvent Event(int itemId) =>
        new(Guid.NewGuid(), CharacterId: 7, ItemId: itemId, Quantity: 2);

    private static CompiledEventPipeline<object> CompilePipeline(EventPipelineExpression pipeline) =>
        EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    private sealed class Provider(Func<object, bool> predicate) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? result)
        {
            _ = subjectType;
            _ = fingerprint;
            result = predicate;
            return true;
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

    private sealed record CacheSubject(bool Flag);
}

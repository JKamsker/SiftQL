using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class FilterCompilerCacheRegressionTests
{
    public static void RunAll()
    {
        FilterCacheRefreshesWhenScopedPrecompiledProviderChanges();
        ImmediateFilterCacheIgnoresHotSinkReferenceIdentity();
        PipelineCacheRefreshesWhenScopedPrecompiledProviderChanges()
            .GetAwaiter()
            .GetResult();
        ParameterizedPlanCacheSeparatesCustomSchemas();
    }

    private static void FilterCacheRefreshesWhenScopedPrecompiledProviderChanges()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(999L));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        CompiledKernel beforeProvider = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered);
        Assert.True(beforeProvider.IsTiered);

        using (PrecompiledTieredProviderRegistry.Register(new Provider(static _ => true)))
        {
            CompiledKernel withProvider = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered);

            Assert.NotSame(beforeProvider, withProvider);
            Assert.False(withProvider.IsTiered);
            Assert.True(withProvider.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1)));
        }
    }

    private static void ImmediateFilterCacheIgnoresHotSinkReferenceIdentity()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        var first = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate with { HotManifestSink = new EqualSink() });
        var second = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate with { HotManifestSink = new EqualSink() });

        Assert.Same(first, second);
    }

    private static async Task PipelineCacheRefreshesWhenScopedPrecompiledProviderChanges()
    {
        var pipeline = EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId)));
        var ev = new ItemUsedEvent(Guid.NewGuid(), CharacterId: 7, ItemId: 100, Quantity: 2);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        CompiledEventPipeline<object> beforeProvider = CompilePipeline(pipeline);
        ProjectedEvent? fallback = await beforeProvider.ProjectAsync(
            ev,
            new object(),
            CancellationToken.None);

        Assert.NotNull(fallback);
        Assert.Equal(100, fallback!.Field(nameof(ItemUsedEvent.ItemId)).Integer);

        using (PrecompiledTieredProviderRegistry.Register(new ProjectionProvider()))
        {
            CompiledEventPipeline<object> withProvider = CompilePipeline(pipeline);
            ProjectedEvent? projected = await withProvider.ProjectAsync(
                ev,
                new object(),
                CancellationToken.None);

            Assert.NotSame(beforeProvider, withProvider);
            Assert.NotNull(projected);
            Assert.True(projected!.TryGetField("provided", out var provided));
            Assert.Equal(777, provided.Integer);
        }

        CompiledEventPipeline<object> afterProvider = CompilePipeline(pipeline);
        ProjectedEvent? afterProjected = await afterProvider.ProjectAsync(
            ev,
            new object(),
            CancellationToken.None);

        Assert.NotNull(afterProjected);
        Assert.False(afterProjected!.TryGetField("provided", out _));
        Assert.Equal(100, afterProjected.Field(nameof(ItemUsedEvent.ItemId)).Integer);
    }

    private static void ParameterizedPlanCacheSeparatesCustomSchemas()
    {
        var expression = FilterExpression.Compare(
            "Flag",
            FilterOperator.Equal,
            FilterValue.From(true) with { ParameterKey = "p0" });

        CompiledKernel falseSchema = FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            expression,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => Schema(static _ => false));
        CompiledKernel trueSchema = FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            expression,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => Schema(static _ => true));

        Assert.False(falseSchema.Matches(new ProjectedEvent()));
        Assert.True(trueSchema.Matches(new ProjectedEvent()));
    }

    private static FilterSchema Schema(Func<object, object?> getter) =>
        new(
            typeof(ProjectedEvent),
            [
                new FilterField(
                    "Flag",
                    typeof(bool),
                    FilterFieldKind.Scalar,
                    getter,
                    ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(true)),
            ]);

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

    private sealed class ProjectionProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? result)
        {
            _ = subjectType;
            _ = fingerprint;
            result = null;
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
                [new ProjectedEventField("provided", ProjectedEventValue.FromScalar(777))];
            return true;
        }
    }

    private sealed class EqualSink : ITieredHotManifestSink
    {
        public void RecordHotFilter(
            Type subjectType,
            FilterExpression expression,
            long evaluations,
            long matches)
        {
            _ = subjectType;
            _ = expression;
            _ = evaluations;
            _ = matches;
        }

        public void RecordHotProjection(
            Type subjectType,
            EventProjectionExpression projection,
            long materializations,
            long payloadWrites)
        {
            _ = subjectType;
            _ = projection;
            _ = materializations;
            _ = payloadWrites;
        }
    }
}

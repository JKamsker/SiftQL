using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class FilterCompilerCacheRegressionTests
{
    public static void RunAll()
    {
        FilterCacheRefreshesWhenScopedPrecompiledProviderChanges();
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
}

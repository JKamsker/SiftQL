using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Tiered;

namespace SiftQL.Parameterized;

internal static class ParameterizedFilterCompiler
{
    public static CompiledKernel Compile(
        FilterSchema schema,
        FilterExpression expression,
        FilterCompilerOptions options,
        TieredFilterPromotionPolicy promotionPolicy,
        bool isBroad,
        Func<string, Exception>? errorFactory)
    {
        ParameterizedFilterPlan plan = ParameterizedFilterPlanCache.GetOrAdd(schema, expression, errorFactory);
        if (options.Mode != FilterCompilationMode.Tiered)
            return new CompiledKernel(plan.Bind(expression), isBroad);

        Func<object, bool> interpreted = FilterInterpretedCompiler.Compile(schema, expression, errorFactory);
        Action<TieredKernelSnapshot>? recordHot = options.HotManifestSink is null
            ? null
            : snapshot => options.HotManifestSink.RecordHotFilter(
                schema.SubjectType,
                expression,
                snapshot.Evaluations,
                snapshot.Matches);
        CompiledKernel? kernel = null;
        var state = new TieredKernelState(
            interpreted,
            () => CompilePromoted(schema, expression, plan, errorFactory),
            promotionPolicy,
            recordHot,
            promoted => kernel!.Promote(promoted));
        kernel = new CompiledKernel(state.Matches, isBroad, state);
        return kernel;
    }

    private static KernelPredicate CompilePromoted(
        FilterSchema schema,
        FilterExpression expression,
        ParameterizedFilterPlan plan,
        Func<string, Exception>? errorFactory)
    {
        string fingerprint = FilterExpressionFingerprint.Create(expression);
        FilterValue[] parameters = FilterExpressionParameters.BindValues(
            expression,
            FilterExpressionParameters.Keys(expression));
        if (PrecompiledTieredProviderRegistry.TryGetParameterizedFilter(
            schema.SubjectType,
            fingerprint,
            parameters,
            out Func<object, bool>? hot))
        {
            return KernelPredicate.FromObject(hot!);
        }

        return FilterExpressionCompiler.TryCompilePredicate(schema, expression, errorFactory) ??
            plan.Bind(expression);
    }
}

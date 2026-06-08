using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Tiered;

internal static class TieredFilterKernelFactory
{
    public static CompiledKernel Create(
        FilterSchema schema,
        FilterExpression expression,
        TieredFilterPromotionPolicy promotionPolicy,
        bool isBroad,
        ITieredHotManifestSink? hotManifestSink,
        Func<string, Exception>? errorFactory)
    {
        FilterExpression expressionSnapshot = FilterExpressionSnapshot.Clone(expression);
        Func<object, bool> interpreted = FilterInterpretedCompiler.Compile(schema, expressionSnapshot, errorFactory);
        string fingerprint = FilterExpressionFingerprint.Create(expressionSnapshot);
        Func<KernelPredicate?> compilePromoted = () =>
            PrecompiledTieredProviderRegistry.TryGetFilter(schema.SubjectType, fingerprint, out var hot)
                ? KernelPredicate.FromObject(hot!)
                : FilterExpressionCompiler.TryCompilePredicate(schema, expressionSnapshot, errorFactory);
        Action<TieredKernelSnapshot>? recordHot = hotManifestSink is null
            ? null
            : report => hotManifestSink.RecordHotFilter(
                schema.SubjectType,
                expressionSnapshot,
                report.Evaluations,
                report.Matches);
        CompiledKernel? kernel = null;
        var state = new TieredKernelState(
            interpreted,
            compilePromoted,
            promotionPolicy,
            recordHot,
            promoted => kernel!.Promote(promoted));
        kernel = new CompiledKernel(state.Matches, isBroad, state);
        return kernel;
    }
}

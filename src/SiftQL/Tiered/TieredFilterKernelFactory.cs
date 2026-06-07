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
        Func<object, bool> interpreted = FilterInterpretedCompiler.Compile(schema, expression, errorFactory);
        Func<KernelPredicate?> compilePromoted = () =>
            FilterExpressionCompiler.TryCompilePredicate(schema, expression, errorFactory);
        Action<TieredKernelSnapshot>? recordHot = hotManifestSink is null
            ? null
            : snapshot => hotManifestSink.RecordHotFilter(
                schema.SubjectType,
                expression,
                snapshot.Evaluations,
                snapshot.Matches);
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

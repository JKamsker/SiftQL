using SiftQL;
using SiftQL.Expressions;
using SiftQL.Tiered;

namespace SiftQL.Compiler;

internal readonly record struct FilterCompilationCacheKey(
    Type SubjectType,
    FilterExpressionKey ExpressionKey,
    FilterCompilationMode Mode,
    int PromotionMinimumEvaluations,
    long PromotionMinimumAgeTicks,
    int PromotionQueueCapacity,
    HotManifestSinkIdentity HotManifestSink)
{
    public static FilterCompilationCacheKey Create(
        Type subjectType,
        FilterExpressionKey expressionKey,
        FilterCompilationMode mode,
        TieredFilterPromotionPolicy promotionPolicy,
        HotManifestSinkIdentity hotManifestSink)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(expressionKey);
        return new FilterCompilationCacheKey(
            subjectType,
            expressionKey,
            mode,
            promotionPolicy.MinimumEvaluations,
            promotionPolicy.MinimumAge.Ticks,
            promotionPolicy.QueueCapacity,
            hotManifestSink);
    }
}

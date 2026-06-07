using SiftQL;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Tiered;

namespace SiftQL.Compiler;

public enum FilterCompilationMode
{
    Immediate,
    Tiered,
}

public sealed record FilterCompilerOptions
{
    private static readonly TimeSpan DefaultPromotionAge = TimeSpan.FromSeconds(5);

    public static FilterCompilerOptions Immediate { get; } = new();
    public static FilterCompilerOptions Tiered { get; } = new()
    {
        Mode = FilterCompilationMode.Tiered,
    };

    public FilterCompilationMode Mode { get; init; }
    public TimeSpan TieredPromotionMinimumAge { get; init; } = DefaultPromotionAge;
    public int? TieredPromotionMinimumEvaluations { get; init; }
    public int TieredPromotionQueueCapacity { get; init; } = 1024;
    public ITieredHotManifestSink? HotManifestSink { get; init; }

    internal TieredFilterPromotionPolicy CreateFilterPromotionPolicy(
        FilterExpression expression)
    {
        if (Mode != FilterCompilationMode.Tiered)
            return default;
        if (TieredPromotionMinimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionMinimumAge),
                "Promotion age must be non-negative.");
        if (TieredPromotionMinimumEvaluations is < 1)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionMinimumEvaluations),
                "Promotion evaluation threshold must be positive.");
        if (TieredPromotionQueueCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionQueueCapacity),
                "Promotion queue capacity must be positive.");

        int evaluations = TieredPromotionMinimumEvaluations ??
            FilterExpressionInspector.PromotionMinimumEvaluations(expression);
        return new TieredFilterPromotionPolicy(
            evaluations,
            TieredPromotionMinimumAge,
            TieredPromotionQueueCapacity);
    }
}

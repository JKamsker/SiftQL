namespace SiftQL.Tiered;

internal readonly record struct TieredFilterPromotionPolicy(
    int MinimumEvaluations,
    TimeSpan MinimumAge,
    int QueueCapacity);

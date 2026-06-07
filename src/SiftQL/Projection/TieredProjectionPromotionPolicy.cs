namespace SiftQL.Projection;

internal readonly record struct TieredProjectionPromotionPolicy(
    int MinimumOperations,
    TimeSpan MinimumAge,
    int QueueCapacity);

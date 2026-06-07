using SiftQL.Hot;

namespace SiftQL.Projection;

public enum ProjectionCompilationMode
{
    Immediate,
    Tiered,
}

public sealed record ProjectionCompilerOptions
{
    private static readonly TimeSpan DefaultPromotionAge = TimeSpan.FromSeconds(5);

    public static ProjectionCompilerOptions Immediate { get; } = new();
    public static ProjectionCompilerOptions Tiered { get; } = new()
    {
        Mode = ProjectionCompilationMode.Tiered,
    };

    public ProjectionCompilationMode Mode { get; init; }
    public TimeSpan TieredPromotionMinimumAge { get; init; } = DefaultPromotionAge;
    public int TieredPromotionMinimumOperations { get; init; } = 10_000;
    public int TieredPromotionQueueCapacity { get; init; } = 1024;
    public ITieredHotManifestSink? HotManifestSink { get; init; }

    internal TieredProjectionPromotionPolicy CreatePromotionPolicy()
    {
        if (Mode != ProjectionCompilationMode.Tiered)
            return default;
        if (TieredPromotionMinimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionMinimumAge),
                "Promotion age must be non-negative.");
        if (TieredPromotionMinimumOperations < 1)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionMinimumOperations),
                "Promotion operation threshold must be positive.");
        if (TieredPromotionQueueCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(TieredPromotionQueueCapacity),
                "Promotion queue capacity must be positive.");

        return new TieredProjectionPromotionPolicy(
            TieredPromotionMinimumOperations,
            TieredPromotionMinimumAge,
            TieredPromotionQueueCapacity);
    }
}

using System.Runtime.CompilerServices;
using SiftQL.Hot;
using SiftQL.Projection;

namespace SiftQL.Compiler;

internal readonly record struct FilterCompilerOptionsCacheKey(
    FilterCompilationMode Mode,
    int? PromotionMinimumEvaluations,
    long PromotionMinimumAgeTicks,
    int PromotionQueueCapacity,
    HotManifestSinkIdentity HotManifestSink)
{
    public static FilterCompilerOptionsCacheKey From(FilterCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Mode == FilterCompilationMode.Tiered
            ? new(
                options.Mode,
                options.TieredPromotionMinimumEvaluations,
                options.TieredPromotionMinimumAge.Ticks,
                options.TieredPromotionQueueCapacity,
                HotManifestSinkIdentity.From(options.HotManifestSink))
            : new(options.Mode, null, 0, 0, default);
    }
}

internal readonly record struct ProjectionCompilerOptionsCacheKey(
    ProjectionCompilationMode Mode,
    int PromotionMinimumOperations,
    long PromotionMinimumAgeTicks,
    int PromotionQueueCapacity,
    HotManifestSinkIdentity HotManifestSink)
{
    public static ProjectionCompilerOptionsCacheKey From(ProjectionCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Mode == ProjectionCompilationMode.Tiered
            ? new(
                options.Mode,
                options.TieredPromotionMinimumOperations,
                options.TieredPromotionMinimumAge.Ticks,
                options.TieredPromotionQueueCapacity,
                HotManifestSinkIdentity.From(options.HotManifestSink))
            : new(options.Mode, 0, 0, 0, default);
    }
}

internal readonly struct HotManifestSinkIdentity : IEquatable<HotManifestSinkIdentity>
{
    private readonly ITieredHotManifestSink? _sink;
    private readonly int _hashCode;

    private HotManifestSinkIdentity(ITieredHotManifestSink? sink)
    {
        _sink = sink;
        _hashCode = sink is null ? 0 : RuntimeHelpers.GetHashCode(sink);
    }

    public static HotManifestSinkIdentity From(ITieredHotManifestSink? sink) =>
        new(sink);

    public bool Equals(HotManifestSinkIdentity other) =>
        ReferenceEquals(_sink, other._sink);

    public override bool Equals(object? obj) =>
        obj is HotManifestSinkIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;
}

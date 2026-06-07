using SiftQL;
using SiftQL.Expressions;
using SiftQL.Tiered;
using SiftQL.Schema;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SiftQL.Compiler;

internal readonly record struct FilterCompilationCacheKey(
    Type SubjectType,
    SchemaFactoryCacheKey SchemaFactory,
    FilterExpressionKey ExpressionKey,
    FilterCompilationMode Mode,
    int PromotionMinimumEvaluations,
    long PromotionMinimumAgeTicks,
    int PromotionQueueCapacity,
    int PrecompiledProviderVersion,
    HotManifestSinkIdentity HotManifestSink)
{
    public static FilterCompilationCacheKey Create(
        Type subjectType,
        Func<Type, FilterSchema> schemaFactory,
        FilterExpressionKey expressionKey,
        FilterCompilationMode mode,
        TieredFilterPromotionPolicy promotionPolicy,
        int precompiledProviderVersion,
        HotManifestSinkIdentity hotManifestSink)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(expressionKey);
        return new FilterCompilationCacheKey(
            subjectType,
            SchemaFactoryCacheKey.From(schemaFactory),
            expressionKey,
            mode,
            promotionPolicy.MinimumEvaluations,
            promotionPolicy.MinimumAge.Ticks,
            promotionPolicy.QueueCapacity,
            precompiledProviderVersion,
            mode == FilterCompilationMode.Tiered ? hotManifestSink : default);
    }
}

internal readonly struct SchemaFactoryCacheKey : IEquatable<SchemaFactoryCacheKey>
{
    private readonly MethodInfo _method;
    private readonly object? _target;
    private readonly int _hashCode;

    private SchemaFactoryCacheKey(MethodInfo method, object? target)
    {
        _method = method;
        _target = target;
        _hashCode = HashCode.Combine(method, target is null ? 0 : RuntimeHelpers.GetHashCode(target));
    }

    public static SchemaFactoryCacheKey From(Func<Type, FilterSchema> schemaFactory) =>
        new(schemaFactory.Method, schemaFactory.Target);

    public bool Equals(SchemaFactoryCacheKey other) =>
        _method == other._method && ReferenceEquals(_target, other._target);

    public override bool Equals(object? obj) =>
        obj is SchemaFactoryCacheKey other && Equals(other);

    public override int GetHashCode() => _hashCode;
}

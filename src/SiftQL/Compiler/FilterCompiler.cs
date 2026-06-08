using System.Collections.Concurrent;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Parameterized;
using SiftQL.Schema;
using SiftQL.Tiered;

namespace SiftQL.Compiler;

public static class FilterCompiler
{
    private const int MaxCachedKernels = 4096;
    private static readonly ConcurrentDictionary<FilterCompilationCacheKey, CompiledKernel> s_kernelCache = new();
    private static int s_kernelCacheCount;

    static FilterCompiler()
    {
        PrecompiledTieredProviderRegistry.Changed += ClearCache;
    }

    public static CompiledKernel Compile(
        Type subjectType,
        FilterExpression? expression,
        Func<string, Exception>? errorFactory = null) =>
        Compile(subjectType, expression, FilterCompilerOptions.Immediate, errorFactory);

    public static CompiledKernel Compile(
        Type subjectType,
        FilterExpression? expression,
        FilterCompilerOptions options,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CompileCached(subjectType, expression, options, errorFactory, FilterSchema.For);
    }

    internal static CompiledKernel CompileUncachedForBenchmarks(
        Type subjectType,
        FilterExpression? expression,
        Func<string, Exception>? errorFactory = null) =>
        CompileUncachedForBenchmarks(
            subjectType,
            expression,
            FilterCompilerOptions.Immediate,
            errorFactory);

    internal static CompiledKernel CompileUncachedForBenchmarks(
        Type subjectType,
        FilterExpression? expression,
        FilterCompilerOptions options,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        expression ??= FilterExpression.Any;
        if (expression.Kind == FilterExpressionKind.Any)
            return CompiledKernel.Any;

        TieredFilterPromotionPolicy policy = options.CreateFilterPromotionPolicy(expression);
        return CompileUncached(
            subjectType,
            expression,
            options,
            policy,
            errorFactory,
            FilterSchema.BuildUncachedForBenchmarks);
    }

    internal static CompiledKernel CompileWithSchema(
        Type subjectType,
        FilterExpression? expression,
        FilterCompilerOptions options,
        Func<string, Exception>? errorFactory,
        Func<Type, FilterSchema> schemaFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaFactory);
        return CompileCached(subjectType, expression, options, errorFactory, schemaFactory);
    }

    private static CompiledKernel CompileCached(
        Type subjectType,
        FilterExpression? expression,
        FilterCompilerOptions options,
        Func<string, Exception>? errorFactory,
        Func<Type, FilterSchema> schemaFactory)
    {
        expression ??= FilterExpression.Any;
        if (expression.Kind == FilterExpressionKind.Any)
            return CompiledKernel.Any;

        bool hasParameters = FilterExpressionParameters.HasParameters(expression);
        FilterExpressionKey expressionKey = FilterExpressionFingerprint.CreateKey(expression);
        string fingerprint = expressionKey.ToString();
        TieredFilterPromotionPolicy promotionPolicy = options.CreateFilterPromotionPolicy(expression);

        if (hasParameters || PrecompiledTieredProviderRegistry.IsolatedScopeActive)
            return CompileCacheMiss(
                subjectType,
                expression,
                options,
                errorFactory,
                schemaFactory,
                hasParameters,
                fingerprint,
                promotionPolicy,
                cacheKey: null);

        var key = FilterCompilationCacheKey.Create(
            subjectType,
            schemaFactory,
            expressionKey,
            options.Mode,
            promotionPolicy,
            PrecompiledTieredProviderRegistry.GlobalVersion,
            FilterSchema.Version,
            HotManifestSinkIdentity.From(options.HotManifestSink));
        if (s_kernelCache.TryGetValue(key, out CompiledKernel? cached))
            return cached;

        return CompileCacheMiss(
            subjectType,
            expression,
            options,
            errorFactory,
            schemaFactory,
            hasParameters,
            fingerprint,
            promotionPolicy,
            key);
    }

    private static CompiledKernel CompileCacheMiss(
        Type subjectType,
        FilterExpression expression,
        FilterCompilerOptions options,
        Func<string, Exception>? errorFactory,
        Func<Type, FilterSchema> schemaFactory,
        bool hasParameters,
        string fingerprint,
        TieredFilterPromotionPolicy promotionPolicy,
        FilterCompilationCacheKey? cacheKey)
    {
        FilterSchema schema = schemaFactory(subjectType);
        bool isBroad = !FilterExpressionInspector.HasSelectiveNode(expression);
        if (PrecompiledTieredProviderRegistry.HasProviders)
        {
            _ = FilterInterpretedCompiler.Compile(schema, expression, errorFactory);
            if (!hasParameters &&
                PrecompiledTieredProviderRegistry.TryGetFilter(subjectType, fingerprint, out var precompiled))
            {
                return new CompiledKernel(precompiled!, isBroad);
            }

            if (hasParameters)
            {
                FilterValue[] parameters = FilterExpressionParameters.BindValues(
                    expression,
                    FilterExpressionParameters.Keys(expression));
                if (PrecompiledTieredProviderRegistry.TryGetParameterizedFilter(
                    subjectType,
                    fingerprint,
                    parameters,
                    out precompiled))
                {
                    return new CompiledKernel(precompiled!, isBroad);
                }
            }
        }

        if (hasParameters)
        {
            return ParameterizedFilterCompiler.Compile(
                schema,
                expression,
                options,
                promotionPolicy,
                isBroad,
                errorFactory);
        }

        if (cacheKey is null)
            return CompileUncached(schema, expression, options, promotionPolicy, errorFactory);

        EnsureCacheCapacity();
        CompiledKernel compiled = CompileUncached(schema, expression, options, promotionPolicy, errorFactory);
        if (s_kernelCache.TryAdd(cacheKey.Value, compiled))
        {
            Interlocked.Increment(ref s_kernelCacheCount);
            return compiled;
        }

        return s_kernelCache.TryGetValue(cacheKey.Value, out CompiledKernel? raced)
            ? raced
            : compiled;
    }

    private static void EnsureCacheCapacity()
    {
        if (Volatile.Read(ref s_kernelCacheCount) < MaxCachedKernels)
            return;

        ClearCache();
    }

    private static CompiledKernel CompileUncached(
        Type subjectType,
        FilterExpression expression,
        FilterCompilerOptions options,
        TieredFilterPromotionPolicy promotionPolicy,
        Func<string, Exception>? errorFactory,
        Func<Type, FilterSchema> schemaFactory)
    {
        var schema = schemaFactory(subjectType);
        return CompileUncached(schema, expression, options, promotionPolicy, errorFactory);
    }

    private static CompiledKernel CompileUncached(
        FilterSchema schema,
        FilterExpression expression,
        FilterCompilerOptions options,
        TieredFilterPromotionPolicy promotionPolicy,
        Func<string, Exception>? errorFactory)
    {
        bool isBroad = !FilterExpressionInspector.HasSelectiveNode(expression);
        if (options.Mode == FilterCompilationMode.Tiered)
            return TieredFilterKernelFactory.Create(
                schema,
                expression,
                promotionPolicy,
                isBroad,
                options.HotManifestSink,
                errorFactory);

        KernelPredicate? expressionPredicate = FilterExpressionCompiler.TryCompilePredicate(
            schema,
            expression,
            errorFactory);
        return expressionPredicate is null
            ? new CompiledKernel(FilterInterpretedCompiler.Compile(schema, expression, errorFactory), isBroad)
            : new CompiledKernel(expressionPredicate, isBroad);
    }

    private static void ClearCache()
    {
        s_kernelCache.Clear();
        Volatile.Write(ref s_kernelCacheCount, 0);
    }
}

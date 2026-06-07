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
        if (!hasParameters &&
            PrecompiledTieredProviderRegistry.TryGetFilter(subjectType, fingerprint, out var precompiled))
        {
            bool isBroad = !FilterExpressionInspector.HasSelectiveNode(expression);
            return new CompiledKernel(precompiled!, isBroad);
        }

        TieredFilterPromotionPolicy promotionPolicy = options.CreateFilterPromotionPolicy(expression);
        if (hasParameters)
        {
            var schema = schemaFactory(subjectType);
            bool isBroad = !FilterExpressionInspector.HasSelectiveNode(expression);
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

            return ParameterizedFilterCompiler.Compile(
                schema,
                expression,
                options,
                promotionPolicy,
                isBroad,
                errorFactory);
        }

        FilterCompilationCacheKey key = FilterCompilationCacheKey.Create(
            subjectType,
            expressionKey,
            options.Mode,
            promotionPolicy,
            HotManifestSinkIdentity.From(options.HotManifestSink));
        if (s_kernelCache.TryGetValue(key, out CompiledKernel? cached))
            return cached;

        if (s_kernelCache.Count >= MaxCachedKernels)
            return CompileUncached(subjectType, expression, options, promotionPolicy, errorFactory, schemaFactory);

        return s_kernelCache.GetOrAdd(
            key,
            static (cacheKey, state) => CompileUncached(
                cacheKey.SubjectType,
                state.Expression,
                state.Options,
                state.PromotionPolicy,
                state.ErrorFactory,
                state.SchemaFactory),
            (Expression: expression,
                Options: options,
                PromotionPolicy: promotionPolicy,
                ErrorFactory: errorFactory,
                SchemaFactory: schemaFactory));
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
}

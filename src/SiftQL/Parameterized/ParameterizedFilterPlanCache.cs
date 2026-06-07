using System.Collections.Concurrent;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Parameterized;

internal static class ParameterizedFilterPlanCache
{
    private const int MaxCachedPlans = 4096;
    private static readonly ConcurrentDictionary<ParameterizedFilterPlanCacheKey, ParameterizedFilterPlan> s_plans = new();
    private static long s_requests;
    private static long s_hits;
    private static long s_misses;

    public static ParameterizedFilterPlan GetOrAdd(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        var key = new ParameterizedFilterPlanCacheKey(
            schema.SubjectType,
            FilterExpressionFingerprint.CreateKey(expression));
        Interlocked.Increment(ref s_requests);
        if (s_plans.TryGetValue(key, out var cached))
        {
            Interlocked.Increment(ref s_hits);
            return cached;
        }

        Interlocked.Increment(ref s_misses);
        if (s_plans.Count >= MaxCachedPlans)
            return ParameterizedFilterPlanBuilder.Build(schema, expression, errorFactory);
        return s_plans.GetOrAdd(
            key,
            static (_, state) => ParameterizedFilterPlanBuilder.Build(
                state.Schema,
                state.Expression,
                state.ErrorFactory),
            (Schema: schema, Expression: expression, ErrorFactory: errorFactory));
    }

    internal static ParameterizedFilterPlanCacheSnapshot Snapshot =>
        new(
            s_plans.Count,
            Interlocked.Read(ref s_requests),
            Interlocked.Read(ref s_hits),
            Interlocked.Read(ref s_misses));

    internal static void ClearForTests()
    {
        s_plans.Clear();
        Interlocked.Exchange(ref s_requests, 0);
        Interlocked.Exchange(ref s_hits, 0);
        Interlocked.Exchange(ref s_misses, 0);
    }
}

internal sealed record ParameterizedFilterPlanCacheSnapshot(
    int Count,
    long Requests,
    long Hits,
    long Misses);

internal readonly record struct ParameterizedFilterPlanCacheKey(
    Type SubjectType,
    FilterExpressionKey ExpressionKey);

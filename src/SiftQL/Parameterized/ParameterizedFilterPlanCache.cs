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
    private static int s_planCount;
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
            schema,
            FilterExpressionFingerprint.CreateKey(expression));
        Interlocked.Increment(ref s_requests);
        if (s_plans.TryGetValue(key, out var cached))
        {
            Interlocked.Increment(ref s_hits);
            return cached;
        }

        Interlocked.Increment(ref s_misses);
        if (Volatile.Read(ref s_planCount) >= MaxCachedPlans)
            ClearPlans();

        ParameterizedFilterPlan plan = ParameterizedFilterPlanBuilder.Build(schema, expression, errorFactory);
        if (s_plans.TryAdd(key, plan))
        {
            Interlocked.Increment(ref s_planCount);
            return plan;
        }

        return s_plans.TryGetValue(key, out ParameterizedFilterPlan? raced)
            ? raced
            : plan;
    }

    internal static ParameterizedFilterPlanCacheSnapshot Snapshot =>
        new(
            Volatile.Read(ref s_planCount),
            Interlocked.Read(ref s_requests),
            Interlocked.Read(ref s_hits),
            Interlocked.Read(ref s_misses));

    internal static void ClearForTests()
    {
        ClearPlans();
        Interlocked.Exchange(ref s_requests, 0);
        Interlocked.Exchange(ref s_hits, 0);
        Interlocked.Exchange(ref s_misses, 0);
    }

    private static void ClearPlans()
    {
        s_plans.Clear();
        Volatile.Write(ref s_planCount, 0);
    }
}

internal sealed record ParameterizedFilterPlanCacheSnapshot(
    int Count,
    long Requests,
    long Hits,
    long Misses);

internal readonly record struct ParameterizedFilterPlanCacheKey(
    Type SubjectType,
    FilterSchema Schema,
    FilterExpressionKey ExpressionKey);

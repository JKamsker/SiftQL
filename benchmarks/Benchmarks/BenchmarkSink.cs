using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Benchmarks;

internal static class BenchmarkSink
{
    private static long s_long;
    private static object? s_object;

    public static void Consume(long value) =>
        Volatile.Write(ref s_long, Volatile.Read(ref s_long) ^ value);

    public static void Consume(object? value) =>
        Volatile.Write(ref s_object, value);

    public static void Consume(ProjectedEvent value)
    {
        Volatile.Write(ref s_object, value);
        Consume(value.Fields.Length + value.Context.Length);
    }
}

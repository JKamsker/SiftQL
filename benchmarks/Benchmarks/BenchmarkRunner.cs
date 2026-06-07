using System.Diagnostics;
using System.Globalization;

namespace SiftQL.Benchmarks;

internal static class BenchmarkRunner
{
    public static void Run(IReadOnlyList<IBenchmarkCase> cases, BenchmarkOptions options)
    {
        PrintEnvironment(options);

        var measurements = new List<BenchmarkMeasurement>();
        foreach (var benchmark in cases)
        {
            int iterations = Math.Max(1, (int)(benchmark.Iterations * options.Scale));
            measurements.Add(Measure(benchmark, iterations, options.Samples));
        }

        PrintResults(measurements);
    }

    private static BenchmarkMeasurement Measure(IBenchmarkCase benchmark, int iterations, int samples)
    {
        int warmupIterations = Math.Min(iterations, 20_000);
        benchmark.Manual(warmupIterations);
        benchmark.Engine(warmupIterations);

        var manual = new BenchmarkSample[samples];
        var engine = new BenchmarkSample[samples];
        for (int i = 0; i < samples; i++)
        {
            manual[i] = MeasureSample(benchmark.Manual, iterations);
            engine[i] = MeasureSample(benchmark.Engine, iterations);
        }

        BenchmarkSample manualMedian = Median(manual);
        BenchmarkSample engineMedian = Median(engine);
        return new BenchmarkMeasurement(
            benchmark.Category,
            benchmark.Name,
            iterations,
            manualMedian.NanosecondsPerOperation,
            engineMedian.NanosecondsPerOperation,
            manualMedian.BytesPerOperation,
            engineMedian.BytesPerOperation);
    }

    private static BenchmarkSample MeasureSample(Action<int> run, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        run(iterations);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        return new BenchmarkSample(
            elapsed.TotalMilliseconds * 1_000_000 / iterations,
            (double)allocated / iterations);
    }

    private static BenchmarkSample Median(BenchmarkSample[] samples)
    {
        Array.Sort(samples, static (left, right) =>
            left.NanosecondsPerOperation.CompareTo(right.NanosecondsPerOperation));
        return samples[samples.Length / 2];
    }

    private static void PrintEnvironment(BenchmarkOptions options)
    {
        Console.WriteLine("SiftQL benchmark");
        Console.WriteLine($"Runtime: {Environment.Version} | OS: {Environment.OSVersion}");
        Console.WriteLine($"Process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Stopwatch frequency: {Stopwatch.Frequency.ToString("N0", CultureInfo.InvariantCulture)} ticks/s");
        Console.WriteLine($"Samples: {options.Samples} | Scale: {options.Scale.ToString("0.###", CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            "Scope: compiled/tiered kernels, projections, registration, dispatch lookup, and projected payload serialization.");
        Console.WriteLine();
    }

    private static void PrintResults(IReadOnlyList<BenchmarkMeasurement> measurements)
    {
        Console.WriteLine(
            "Category   Scenario                  Iterations   Manual ns   Engine ns   Overhead ns   Ratio    Manual B   Engine B   Extra B");
        Console.WriteLine(new string('-', 127));
        foreach (var item in measurements)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{item.Category,-10} {item.Name,-25} {item.Iterations,10:N0} " +
                $"{item.ManualNanoseconds,11:0.00} {item.EngineNanoseconds,11:0.00} " +
                $"{item.OverheadNanoseconds,13:0.00} {item.Ratio,7:0.00}x " +
                $"{item.ManualBytes,9:0.0} {item.EngineBytes,10:0.0} {item.ExtraBytes,9:0.0}"));
        }
    }
}

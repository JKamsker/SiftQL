namespace SiftQL.Benchmarks;

internal interface IBenchmarkCase
{
    string Category { get; }
    string Name { get; }
    int Iterations { get; }
    void Manual(int iterations);
    void Engine(int iterations);
}

internal readonly record struct BenchmarkOptions(int Samples, double Scale)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        int samples = 5;
        double scale = 1;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--samples=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg["--samples=".Length..], out int parsedSamples))
            {
                samples = Math.Max(1, parsedSamples);
            }
            else if (arg.StartsWith("--scale=", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(
                    arg["--scale=".Length..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double parsedScale))
            {
                scale = Math.Clamp(parsedScale, 0.01, 100);
            }
        }

        return new BenchmarkOptions(samples, scale);
    }
}

internal readonly record struct BenchmarkMeasurement(
    string Category,
    string Name,
    int Iterations,
    double ManualNanoseconds,
    double EngineNanoseconds,
    double ManualBytes,
    double EngineBytes)
{
    public double OverheadNanoseconds => EngineNanoseconds - ManualNanoseconds;
    public double ExtraBytes => EngineBytes - ManualBytes;
    public double Ratio => ManualNanoseconds <= 0 ? double.PositiveInfinity : EngineNanoseconds / ManualNanoseconds;
}

internal readonly record struct BenchmarkSample(double NanosecondsPerOperation, double BytesPerOperation);

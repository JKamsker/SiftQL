// removed: game-specific value types
// removed: game-specific events
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Tiered;

namespace SiftQL.Benchmarks;

internal sealed class TieredInterpretedFilterCase : IBenchmarkCase
{
    private const int Mask = TieredFilterBenchmarkData.Mask;
    private readonly DamageDealtEvent[] _events = TieredFilterBenchmarkData.CreateEvents();
    private readonly CompiledKernel _kernel = FilterCompiler.Compile(
        typeof(DamageDealtEvent),
        TieredFilterBenchmarkData.Filter,
        FilterCompilerOptions.Tiered with { TieredPromotionMinimumEvaluations = int.MaxValue });
    private readonly CompiledKernelMatcher<DamageDealtEvent> _matcher;

    public TieredInterpretedFilterCase()
    {
        _matcher = _kernel.CreateMatcher<DamageDealtEvent>();
    }

    public string Category => "Filter";
    public string Name => "tiered interpreted 4 clauses";
    public int Iterations => 5_000_000;

    public void Manual(int iterations) =>
        TieredFilterBenchmarkData.RunManual(_events, iterations);

    public void Engine(int iterations) =>
        TieredFilterBenchmarkData.RunKernel(_events, _matcher, iterations, Mask);
}

internal sealed class TieredPromotedFilterCase : IBenchmarkCase
{
    private const int Mask = TieredFilterBenchmarkData.Mask;
    private readonly DamageDealtEvent[] _events = TieredFilterBenchmarkData.CreateEvents();
    private readonly CompiledKernel _kernel;
    private readonly CompiledKernelMatcher<DamageDealtEvent> _matcher;

    public TieredPromotedFilterCase()
    {
        _kernel = FilterCompiler.Compile(
            typeof(DamageDealtEvent),
            TieredFilterBenchmarkData.Filter,
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
            });
        _matcher = _kernel.CreateMatcher<DamageDealtEvent>();
        TieredFilterBenchmarkData.Promote(_kernel, _events[0]);
    }

    public string Category => "Filter";
    public string Name => "tiered promoted 4 clauses";
    public int Iterations => 10_000_000;

    public void Manual(int iterations) =>
        TieredFilterBenchmarkData.RunManual(_events, iterations);

    public void Engine(int iterations) =>
        TieredFilterBenchmarkData.RunKernel(_events, _matcher, iterations, Mask);
}

internal sealed class TieredFilterRegistrationCase : IBenchmarkCase
{
    public string Category => "Registration";
    public string Name => "tiered cold 4-clause filter";
    public int Iterations => 200;

    public void Manual(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            Func<object, bool> predicate = static subject =>
                TieredFilterBenchmarkData.Matches((DamageDealtEvent)subject);
            BenchmarkSink.Consume(predicate);
        }
    }

    public void Engine(int iterations)
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumEvaluations = int.MaxValue,
        };
        for (int i = 0; i < iterations; i++)
        {
            CompiledKernel kernel = FilterCompiler.CompileUncachedForBenchmarks(
                typeof(DamageDealtEvent),
                TieredFilterBenchmarkData.Filter,
                options);
            BenchmarkSink.Consume(kernel);
        }
    }
}

internal static class TieredFilterBenchmarkData
{
    public const int Mask = 1023;

    public static readonly FilterExpression Filter = FilterExpression.And(
        FilterExpression.Compare(nameof(DamageDealtEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(500L)),
        FilterExpression.Compare(nameof(DamageDealtEvent.Critical), FilterOperator.Equal, FilterValue.From(true)),
        FilterExpression.Compare(nameof(DamageDealtEvent.DamageType), FilterOperator.Equal, FilterValue.From("Skill")),
        FilterExpression.Compare(nameof(DamageDealtEvent.MapId), FilterOperator.Equal, FilterValue.From(1L)));

    public static DamageDealtEvent[] CreateEvents() =>
        Enumerable.Range(0, Mask + 1)
            .Select(static index => new DamageDealtEvent(
                Guid.NewGuid(),
                CharacterId: 20 + index,
                TargetId: 456 + index,
                MapId: index % 5 == 0 ? 2 : 1,
                Damage: index % 3 == 0 ? 250 : 1_250,
                DamageType: index % 11 == 0 ? "Dot" : "Skill",
                Critical: index % 7 != 0,
                Skill: new SkillRef(1, "Strike")))
            .ToArray();

    public static void RunManual(DamageDealtEvent[] items, int iterations)
    {
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            if (Matches(items[i & Mask]))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    public static void RunKernel(
        DamageDealtEvent[] items,
        CompiledKernelMatcher<DamageDealtEvent> matcher,
        int iterations,
        int mask)
    {
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            if (matcher.Matches(items[i & mask]))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    public static bool Matches(DamageDealtEvent item) =>
        item.Damage >= 500 &&
        item.Critical &&
        item.DamageType == "Skill" &&
        item.MapId == 1;

    public static void Promote(CompiledKernel kernel, DamageDealtEvent sample)
    {
        for (int i = 0; i < 500; i++)
        {
            kernel.Matches(sample);
            if (kernel.TieredSnapshot?.Tier == TieredKernelTier.Compiled)
                return;

            Thread.Sleep(1);
        }

        throw new InvalidOperationException("Tiered benchmark kernel did not promote.");
    }
}

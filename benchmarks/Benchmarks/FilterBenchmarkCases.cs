// removed: game-specific value types
// removed: game-specific events
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class SimpleFilterCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private readonly ItemUsedEvent[] _events = CreateEvents();
    private readonly CompiledKernelMatcher<ItemUsedEvent> _matcher = FilterCompiler.Compile(
        typeof(ItemUsedEvent),
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L)))
        .CreateMatcher<ItemUsedEvent>();

    public string Category => "Filter";
    public string Name => "1 exact scalar";
    public int Iterations => 20_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (item.ItemId == 100)
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _events;
        var matcher = _matcher;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (matcher.Matches(item))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    private static ItemUsedEvent[] CreateEvents() =>
        Enumerable.Range(0, Mask + 1)
            .Select(static index => new ItemUsedEvent(Guid.NewGuid(), 10 + index, 1, index % 2 == 0 ? 100 : 101, "Item", 2))
            .ToArray();
}

internal sealed class ScalarFilterCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private readonly DamageDealtEvent[] _events = CreateEvents();
    private readonly CompiledKernelMatcher<DamageDealtEvent> _matcher = FilterCompiler.Compile(
        typeof(DamageDealtEvent),
        FilterExpression.And(
            FilterExpression.Compare(nameof(DamageDealtEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(500L)),
            FilterExpression.Compare(nameof(DamageDealtEvent.Critical), FilterOperator.Equal, FilterValue.From(true)),
            FilterExpression.Compare(nameof(DamageDealtEvent.DamageType), FilterOperator.Equal, FilterValue.From("Skill")),
            FilterExpression.Compare(nameof(DamageDealtEvent.MapId), FilterOperator.Equal, FilterValue.From(1L))))
        .CreateMatcher<DamageDealtEvent>();

    public string Category => "Filter";
    public string Name => "4 scalar clauses";
    public int Iterations => 10_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (item.Damage >= 500 &&
                item.Critical &&
                item.DamageType == "Skill" &&
                item.MapId == 1)
            {
                matches++;
            }
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _events;
        var matcher = _matcher;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (matcher.Matches(item))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    private static DamageDealtEvent[] CreateEvents() =>
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
}

internal sealed class ComplexFilterCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private readonly ScalarArrayEvent[] _events = CreateEvents();
    private readonly CompiledKernelMatcher<ScalarArrayEvent> _matcher = FilterCompiler.Compile(
        typeof(ScalarArrayEvent),
        FilterExpression.And(
            FilterExpression.Compare(nameof(ScalarArrayEvent.Accepted), FilterOperator.Equal, FilterValue.From(true)),
            FilterExpression.In(nameof(ScalarArrayEvent.MapId), [FilterValue.From(40L), FilterValue.From(41L), FilterValue.From(42L)]),
            FilterExpression.Compare(nameof(ScalarArrayEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(1_200L)),
            FilterExpression.Compare("Skill.Id", FilterOperator.Equal, FilterValue.From(777L)),
            FilterExpression.Contains(nameof(ScalarArrayEvent.SkillIds), FilterValue.From(999L)),
            FilterExpression.Contains(nameof(ScalarArrayEvent.Tags), FilterValue.From("pvp"))))
        .CreateMatcher<ScalarArrayEvent>();

    public string Category => "Filter";
    public string Name => "in + 2 arrays";
    public int Iterations => 5_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (item.Accepted &&
                (item.MapId == 40 || item.MapId == 41 || item.MapId == 42) &&
                item.Damage >= 1_200 &&
                item.Skill.Id == 777 &&
                Array.IndexOf(item.SkillIds, 999) >= 0 &&
                Array.IndexOf(item.Tags, "pvp") >= 0)
            {
                matches++;
            }
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _events;
        var matcher = _matcher;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (matcher.Matches(item))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    private static ScalarArrayEvent[] CreateEvents() =>
        Enumerable.Range(0, Mask + 1)
            .Select(static index => new ScalarArrayEvent(
                Guid.NewGuid(),
                CharacterId: 10 + index,
                MapId: index % 13 == 0 ? 99 : 42,
                Damage: index % 5 == 0 ? 800 : 1_500,
                Accepted: index % 17 != 0,
                new SkillRef(index % 19 == 0 ? 700 : 777, "Skill"),
                index % 23 == 0 ? [111, 222, 333] : [111, 222, 999, 333],
                index % 29 == 0 ? ["quest", "siege"] : ["quest", "pvp", "siege"]))
            .ToArray();
}

internal sealed class FilterRegistrationCase : IBenchmarkCase
{
    private static readonly FilterExpression s_filter = FilterExpression.And(
        FilterExpression.Compare(nameof(DamageDealtEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(500L)),
        FilterExpression.Compare(nameof(DamageDealtEvent.Critical), FilterOperator.Equal, FilterValue.From(true)),
        FilterExpression.Compare(nameof(DamageDealtEvent.DamageType), FilterOperator.Equal, FilterValue.From("Skill")),
        FilterExpression.Compare(nameof(DamageDealtEvent.MapId), FilterOperator.Equal, FilterValue.From(1L)));

    public string Category => "Registration";
    public string Name => "compile 4-clause filter";
    public int Iterations => 200;

    public void Manual(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            Func<object, bool> predicate = static subject =>
            {
                var item = (DamageDealtEvent)subject;
                return item.Damage >= 500 &&
                    item.Critical &&
                    item.DamageType == "Skill" &&
                    item.MapId == 1;
            };
            BenchmarkSink.Consume(predicate);
        }
    }

    public void Engine(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            CompiledKernel kernel = FilterCompiler.CompileUncachedForBenchmarks(typeof(DamageDealtEvent), s_filter);
            BenchmarkSink.Consume(kernel);
        }
    }
}

internal sealed class PluginFilterRegistrationCase : IBenchmarkCase
{
    private static readonly FilterExpression s_filter = FilterExpression.And(
        FilterExpression.Compare(nameof(ScalarArrayEvent.Accepted), FilterOperator.Equal, FilterValue.From(true)),
        FilterExpression.Compare(nameof(ScalarArrayEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(1_200L)),
        FilterExpression.Compare("Skill.Id", FilterOperator.Equal, FilterValue.From(777L)),
        FilterExpression.Contains(nameof(ScalarArrayEvent.SkillIds), FilterValue.From(999L)));

    public string Category => "Registration";
    public string Name => "compile plugin-owned filter";
    public int Iterations => 200;

    public void Manual(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            Func<object, bool> predicate = static subject =>
            {
                var item = (ScalarArrayEvent)subject;
                return item.Accepted &&
                    item.Damage >= 1_200 &&
                    item.Skill.Id == 777 &&
                    Array.IndexOf(item.SkillIds, 999) >= 0;
            };
            BenchmarkSink.Consume(predicate);
        }
    }

    public void Engine(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            CompiledKernel kernel = FilterCompiler.CompileUncachedForBenchmarks(typeof(ScalarArrayEvent), s_filter);
            BenchmarkSink.Consume(kernel);
        }
    }
}

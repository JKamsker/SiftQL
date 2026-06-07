// removed: game-specific value types
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class ClauseOrderingFilterCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private readonly ScalarArrayEvent[] _events = CreateEvents();
    private readonly CompiledKernelMatcher<ScalarArrayEvent> _matcher = FilterCompiler.Compile(
        typeof(ScalarArrayEvent),
        FilterExpression.And(
            FilterExpression.Contains(nameof(ScalarArrayEvent.Tags), FilterValue.From("pvp")),
            FilterExpression.Contains(nameof(ScalarArrayEvent.SkillIds), FilterValue.From(999L)),
            FilterExpression.Compare(nameof(ScalarArrayEvent.Accepted), FilterOperator.Equal, FilterValue.From(true)),
            FilterExpression.Compare(nameof(ScalarArrayEvent.MapId), FilterOperator.Equal, FilterValue.From(42L))))
        .CreateMatcher<ScalarArrayEvent>();

    public string Category => "Filter";
    public string Name => "ordered expensive clauses";
    public int Iterations => 5_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (item.Accepted &&
                item.MapId == 42 &&
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
                MapId: index % 5 == 0 ? 42 : 99,
                Damage: 1_500,
                Accepted: index % 3 == 0,
                new SkillRef(777, "Skill"),
                index % 7 == 0 ? [111, 222, 999] : [111, 222, 333],
                index % 11 == 0 ? ["quest", "pvp"] : ["quest", "siege"]))
            .ToArray();
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class CachedFilterRegistrationCase : IBenchmarkCase
{
    private static readonly Func<object, bool> s_manualPredicate = ManualPredicate;
    private static readonly FilterExpression s_filter = FilterExpression.And(
        FilterExpression.Compare(nameof(DamageDealtEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(500L)),
        FilterExpression.Compare(nameof(DamageDealtEvent.Critical), FilterOperator.Equal, FilterValue.From(true)),
        FilterExpression.Compare(nameof(DamageDealtEvent.DamageType), FilterOperator.Equal, FilterValue.From("Skill")),
        FilterExpression.Compare(nameof(DamageDealtEvent.MapId), FilterOperator.Equal, FilterValue.From(1L)));

    public string Category => "Registration";
    public string Name => "cached 4-clause filter";
    public int Iterations => 500_000;

    public void Manual(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(s_manualPredicate);
    }

    public void Engine(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(FilterCompiler.Compile(typeof(DamageDealtEvent), s_filter));
    }

    private static bool ManualPredicate(object subject)
    {
        var item = (DamageDealtEvent)subject;
        return item.Damage >= 500 &&
            item.Critical &&
            item.DamageType == "Skill" &&
            item.MapId == 1;
    }
}

internal sealed class CachedPluginFilterRegistrationCase : IBenchmarkCase
{
    private static readonly Func<object, bool> s_manualPredicate = ManualPredicate;
    private static readonly FilterExpression s_filter = FilterExpression.And(
        FilterExpression.Compare(nameof(ScalarArrayEvent.Accepted), FilterOperator.Equal, FilterValue.From(true)),
        FilterExpression.Compare(nameof(ScalarArrayEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(1_200L)),
        FilterExpression.Compare("Skill.Id", FilterOperator.Equal, FilterValue.From(777L)),
        FilterExpression.Contains(nameof(ScalarArrayEvent.SkillIds), FilterValue.From(999L)));

    public string Category => "Registration";
    public string Name => "cached plugin-owned filter";
    public int Iterations => 500_000;

    public void Manual(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(s_manualPredicate);
    }

    public void Engine(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(FilterCompiler.Compile(typeof(ScalarArrayEvent), s_filter));
    }

    private static bool ManualPredicate(object subject)
    {
        var item = (ScalarArrayEvent)subject;
        return item.Accepted &&
            item.Damage >= 1_200 &&
            item.Skill.Id == 777 &&
            Array.IndexOf(item.SkillIds, 999) >= 0;
    }
}

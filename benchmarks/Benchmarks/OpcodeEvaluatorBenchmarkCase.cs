// removed: game-specific value types
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class OpcodeEvaluatorCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private readonly ScalarArrayEvent[] _events = CreateEvents();
    private readonly object[] _objects;
    private readonly CompiledKernel _compiled = FilterCompiler.Compile(typeof(ScalarArrayEvent), Filter());
    private readonly OpcodeProgram _opcode = OpcodeProgram.ComplexFilter();

    public OpcodeEvaluatorCase()
    {
        _objects = _events.Cast<object>().ToArray();
    }

    public string Category => "Evaluator";
    public string Name => "opcode residual complex";
    public int Iterations => 5_000_000;

    public void Manual(int iterations)
    {
        var items = _objects;
        var kernel = _compiled;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            object item = items[i & Mask];
            if (kernel.Matches(item))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    public void Engine(int iterations)
    {
        var items = _objects;
        var opcode = _opcode;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            object item = items[i & Mask];
            if (opcode.Matches(item))
                matches++;
        }

        BenchmarkSink.Consume(matches);
    }

    private static FilterExpression Filter() =>
        FilterExpression.And(
            FilterExpression.Compare(nameof(ScalarArrayEvent.Accepted), FilterOperator.Equal, FilterValue.From(true)),
            FilterExpression.In(nameof(ScalarArrayEvent.MapId), [FilterValue.From(40L), FilterValue.From(41L), FilterValue.From(42L)]),
            FilterExpression.Compare(nameof(ScalarArrayEvent.Damage), FilterOperator.GreaterThanOrEqual, FilterValue.From(1_200L)),
            FilterExpression.Compare("Skill.Id", FilterOperator.Equal, FilterValue.From(777L)),
            FilterExpression.Contains(nameof(ScalarArrayEvent.SkillIds), FilterValue.From(999L)),
            FilterExpression.Contains(nameof(ScalarArrayEvent.Tags), FilterValue.From("pvp")));

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

    private sealed class OpcodeProgram
    {
        private readonly Opcode[] _opcodes;

        private OpcodeProgram(Opcode[] opcodes) => _opcodes = opcodes;

        public static OpcodeProgram ComplexFilter() =>
            new(
            [
                Opcode.Accepted,
                Opcode.MapIdIn,
                Opcode.DamageAtLeast,
                Opcode.SkillId,
                Opcode.ContainsSkillId,
                Opcode.ContainsTag,
            ]);

        public bool Matches(object subject)
        {
            var item = (ScalarArrayEvent)subject;
            for (int i = 0; i < _opcodes.Length; i++)
            {
                if (!Matches(item, _opcodes[i]))
                    return false;
            }

            return true;
        }

        private static bool Matches(ScalarArrayEvent item, Opcode opcode) =>
            opcode switch
            {
                Opcode.Accepted => item.Accepted,
                Opcode.MapIdIn => item.MapId is 40 or 41 or 42,
                Opcode.DamageAtLeast => item.Damage >= 1_200,
                Opcode.SkillId => item.Skill.Id == 777,
                Opcode.ContainsSkillId => Array.IndexOf(item.SkillIds, 999) >= 0,
                Opcode.ContainsTag => Array.IndexOf(item.Tags, "pvp") >= 0,
                _ => false,
            };
    }

    private enum Opcode
    {
        Accepted,
        MapIdIn,
        DamageAtLeast,
        SkillId,
        ContainsSkillId,
        ContainsTag,
    }
}

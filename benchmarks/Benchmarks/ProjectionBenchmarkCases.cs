// removed: game-specific value types
// removed: game-specific events
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Benchmarks;

internal sealed class TwoFieldProjectionCase : IBenchmarkCase
{
    private static readonly string s_eventType = typeof(ItemUsedEvent).FullName ?? nameof(ItemUsedEvent);
    private readonly ItemUsedEvent _event = new(Guid.NewGuid(), 10, 1, 100, "Potion", 2);
    private readonly CompiledProjection<object> _projection = ProjectionCompiler.Compile<object>(
        typeof(ItemUsedEvent),
        EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
        RejectInclude);
    private readonly object _context = new();

    public string Category => "Projection";
    public string Name => "2 fields";
    public int Iterations => 1_000_000;

    public void Manual(int iterations)
    {
        var item = _event;
        for (int i = 0; i < iterations; i++)
        {
            BenchmarkSink.Consume(new ProjectedEvent
            {
                EventType = s_eventType,
                EventName = nameof(ItemUsedEvent),
                Fields =
                [
                    ProjectedValues.Field(nameof(ItemUsedEvent.ItemId), ProjectedValues.Integer(item.ItemId)),
                    ProjectedValues.Field(nameof(ItemUsedEvent.Quantity), ProjectedValues.Integer(item.Quantity)),
                ],
            });
        }
    }

    public void Engine(int iterations)
    {
        object item = _event;
        var projection = _projection;
        object context = _context;
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(projection.ProjectAsync(item, context, CancellationToken.None).GetAwaiter().GetResult());
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

internal sealed class DefaultProjectionCase : IBenchmarkCase
{
    private static readonly string s_eventType = typeof(DamageDealtEvent).FullName ?? nameof(DamageDealtEvent);
    private readonly DamageDealtEvent _event = new(
        Guid.NewGuid(),
        CharacterId: 123,
        TargetId: 456,
        MapId: 1,
        Damage: 1_250,
        DamageType: "Skill",
        Critical: true,
        Skill: new SkillRef(1, "Strike"));
    private readonly CompiledProjection<object> _projection = ProjectionCompiler.Compile<object>(
        typeof(DamageDealtEvent),
        EventProjectionExpression.Default,
        RejectInclude);
    private readonly object _context = new();

    public string Category => "Projection";
    public string Name => "default fields";
    public int Iterations => 500_000;

    public void Manual(int iterations)
    {
        var item = _event;
        for (int i = 0; i < iterations; i++)
        {
            BenchmarkSink.Consume(new ProjectedEvent
            {
                EventType = s_eventType,
                EventName = nameof(DamageDealtEvent),
                Fields =
                [
                    ProjectedValues.Field(nameof(DamageDealtEvent.CharacterId), ProjectedValues.Integer(item.CharacterId)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.Critical), ProjectedValues.Boolean(item.Critical)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.Damage), ProjectedValues.Integer(item.Damage)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.DamageType), ProjectedValues.String(item.DamageType)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.EventId), ProjectedValues.Guid(item.EventId)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.MapId), ProjectedValues.Integer(item.MapId)),
                    ProjectedValues.Field(nameof(DamageDealtEvent.TargetId), ProjectedValues.Integer(item.TargetId)),
                ],
            });
        }
    }

    public void Engine(int iterations)
    {
        object item = _event;
        var projection = _projection;
        object context = _context;
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(projection.ProjectAsync(item, context, CancellationToken.None).GetAwaiter().GetResult());
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

internal sealed class IncludeProjectionCase : IBenchmarkCase
{
    private const string NearbyIntrinsic = "bench.nearby";
    private static readonly string s_eventType = typeof(ScalarArrayEvent).FullName ?? nameof(ScalarArrayEvent);
    private readonly BenchmarkProjectionContext _context = new(ProjectedValues.NearbyPlayers());
    private readonly ScalarArrayEvent _event = new(
        Guid.NewGuid(),
        CharacterId: 10,
        MapId: 42,
        Damage: 1_500,
        Accepted: true,
        new SkillRef(777, "FireBlast"),
        [111, 222, 999, 333],
        ["quest", "pvp", "siege"]);
    private readonly CompiledProjection<BenchmarkProjectionContext> _projection = ProjectionCompiler.Compile<BenchmarkProjectionContext>(
        typeof(ScalarArrayEvent),
        EventProjectionExpression.Default
            .WithFields(
            [
                new EventProjectionField(nameof(ScalarArrayEvent.CharacterId)),
                new EventProjectionField(nameof(ScalarArrayEvent.Damage)),
                new EventProjectionField("Skill.Id", "SkillId"),
            ])
            .WithIncludes([new EventProjectionInclude(NearbyIntrinsic, "nearby")]),
        CompileInclude);

    public string Category => "Projection";
    public string Name => "3 fields + include";
    public int Iterations => 500_000;

    public void Manual(int iterations)
    {
        var item = _event;
        var context = _context;
        for (int i = 0; i < iterations; i++)
        {
            BenchmarkSink.Consume(new ProjectedEvent
            {
                EventType = s_eventType,
                EventName = nameof(ScalarArrayEvent),
                Fields =
                [
                    ProjectedValues.Field(nameof(ScalarArrayEvent.CharacterId), ProjectedValues.Integer(item.CharacterId)),
                    ProjectedValues.Field(nameof(ScalarArrayEvent.Damage), ProjectedValues.Integer(item.Damage)),
                    ProjectedValues.Field("SkillId", ProjectedValues.Integer(item.Skill.Id)),
                ],
                Context = [ProjectedValues.Field("nearby", context.NearbyPlayers)],
            });
        }
    }

    public void Engine(int iterations)
    {
        object item = _event;
        var context = _context;
        var projection = _projection;
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(projection.ProjectAsync(item, context, CancellationToken.None).GetAwaiter().GetResult());
    }

    private static CompiledProjection<BenchmarkProjectionContext>.IncludeProjector CompileInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        if (!string.Equals(include.Intrinsic, NearbyIntrinsic, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");

        return new CompiledProjection<BenchmarkProjectionContext>.IncludeProjector(
            include.ResultName,
            static (_, context, _) => ValueTask.FromResult(context.NearbyPlayers));
    }
}

internal sealed class FilterProjectionPipelineCase : IBenchmarkCase
{
    private static readonly string s_eventType = typeof(ItemUsedEvent).FullName ?? nameof(ItemUsedEvent);
    private readonly ItemUsedEvent _event = new(Guid.NewGuid(), 10, 1, 100, "Potion", 2);
    private readonly CompiledKernel _kernel = FilterCompiler.Compile(
        typeof(ItemUsedEvent),
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L)));
    private readonly CompiledKernelMatcher<ItemUsedEvent> _matcher;
    private readonly CompiledProjection<object> _projection = ProjectionCompiler.Compile<object>(
        typeof(ItemUsedEvent),
        EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
        RejectInclude);
    private readonly object _context = new();

    public FilterProjectionPipelineCase()
    {
        _matcher = _kernel.CreateMatcher<ItemUsedEvent>();
    }

    public string Category => "Pipeline";
    public string Name => "filter + 2 fields";
    public int Iterations => 1_000_000;

    public void Manual(int iterations)
    {
        var item = _event;
        for (int i = 0; i < iterations; i++)
        {
            if (item.ItemId != 100)
                continue;

            BenchmarkSink.Consume(new ProjectedEvent
            {
                EventType = s_eventType,
                EventName = nameof(ItemUsedEvent),
                Fields =
                [
                    ProjectedValues.Field(nameof(ItemUsedEvent.ItemId), ProjectedValues.Integer(item.ItemId)),
                    ProjectedValues.Field(nameof(ItemUsedEvent.Quantity), ProjectedValues.Integer(item.Quantity)),
                ],
            });
        }
    }

    public void Engine(int iterations)
    {
        var item = _event;
        object subject = item;
        object context = _context;
        var matcher = _matcher;
        var projection = _projection;
        for (int i = 0; i < iterations; i++)
        {
            if (matcher.Matches(item))
                BenchmarkSink.Consume(projection.ProjectAsync(subject, context, CancellationToken.None).GetAwaiter().GetResult());
        }
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

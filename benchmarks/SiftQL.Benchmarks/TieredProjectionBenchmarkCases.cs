// removed: game-specific events
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Tiered;

namespace SiftQL.Benchmarks;

internal sealed class TieredTwoFieldProjectionCase : IBenchmarkCase
{
    private static readonly string s_eventType = typeof(ItemUsedEvent).FullName ?? nameof(ItemUsedEvent);
    private readonly ItemUsedEvent _event = new(Guid.NewGuid(), 10, 1, 100, "Potion", 2);
    private readonly object _context = new();
    private readonly CompiledProjection<object> _projection = ProjectionCompiler.Compile<object>(
        typeof(ItemUsedEvent),
        EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
        RejectInclude,
        ProjectionCompilerOptions.Tiered with { TieredPromotionMinimumOperations = int.MaxValue });

    public string Category => "Projection";
    public string Name => "tiered interpreted 2 fields";
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
        object context = _context;
        var projection = _projection;
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

internal sealed class TieredPromotedTwoFieldProjectionCase : IBenchmarkCase
{
    private static readonly string s_eventType = typeof(ItemUsedEvent).FullName ?? nameof(ItemUsedEvent);
    private readonly ItemUsedEvent _event = new(Guid.NewGuid(), 10, 1, 100, "Potion", 2);
    private readonly object _context = new();
    private readonly CompiledProjection<object> _projection;

    public TieredPromotedTwoFieldProjectionCase()
    {
        _projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId), nameof(ItemUsedEvent.Quantity)),
            RejectInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
            });
        Promote(_projection, _event, _context);
    }

    public string Category => "Projection";
    public string Name => "tiered promoted 2 fields";
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
        object context = _context;
        var projection = _projection;
        for (int i = 0; i < iterations; i++)
            BenchmarkSink.Consume(projection.ProjectAsync(item, context, CancellationToken.None).GetAwaiter().GetResult());
    }

    private static void Promote(
        CompiledProjection<object> projection,
        object item,
        object context)
    {
        for (int i = 0; i < 500; i++)
        {
            projection.ProjectAsync(item, context, CancellationToken.None).GetAwaiter().GetResult();
            if (projection.TieredSnapshot?.Tier == TieredProjectionTier.Compiled)
                return;

            Thread.Sleep(1);
        }

        throw new InvalidOperationException("Tiered projection benchmark did not promote.");
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;

namespace SiftQL.Benchmarks;

internal sealed class LargeInFilterCase : IBenchmarkCase
{
    private const int Mask = 1023;
    private static readonly int[] s_allowedTokens = Enumerable.Range(0, 32).Select(static value => value * 2).ToArray();
    private static readonly HashSet<int> s_allowedLookup = new(s_allowedTokens);
    private readonly LargeInEvent[] _events = CreateEvents();
    private readonly CompiledKernelMatcher<LargeInEvent> _matcher = FilterCompiler.Compile(
        typeof(LargeInEvent),
        FilterExpression.In(
            nameof(LargeInEvent.Token),
            s_allowedTokens.Select(static value => FilterValue.From(value)).ToArray()))
        .CreateMatcher<LargeInEvent>();

    public string Category => "Filter";
    public string Name => "32-value in";
    public int Iterations => 10_000_000;

    public void Manual(int iterations)
    {
        var items = _events;
        var lookup = s_allowedLookup;
        long matches = 0;
        for (int i = 0; i < iterations; i++)
        {
            var item = items[i & Mask];
            if (lookup.Contains(item.Token))
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

    private static LargeInEvent[] CreateEvents() =>
        Enumerable.Range(0, Mask + 1)
            .Select(static index => new LargeInEvent(Guid.NewGuid(), index % 96))
            .ToArray();
}

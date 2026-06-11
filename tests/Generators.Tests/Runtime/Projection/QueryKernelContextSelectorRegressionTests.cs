using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextSelectorRegressionTests
{
    [Fact]
    public async Task ContextOnlySelectorDoesNotDefaultProjectWholeSource()
    {
        QueryKernel<WideContextOnlyEvent> query = QueryKernel
            .For<WideContextOnlyEvent, WideContext>()
            .Select(static (_, ctx) => new { Label = ctx.Label() })
            .ToQueryKernel();

        CompiledEventPipeline<WideContext> compiled = EventPipelineCompiler.Compile<WideContext>(
            typeof(WideContextOnlyEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new WideContextOnlyEvent(),
            new WideContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Single(projected!.Fields);
        Assert.Equal("ctx", projected.Field("Label").String);
    }

    [Fact]
    public void ReenteredContextSelectorDeduplicatesLiteralIncludes()
    {
        QueryKernel<LiteralContextEvent> query = QueryKernel
            .For<LiteralContextEvent, LiteralContext>()
            .Select(static (_, ctx) => new { Value = ctx.Echo("same") })
            .ToQueryKernel()
            .WithContext<LiteralContextEvent, LiteralContext>()
            .Select(static (_, ctx) => new { Value = ctx.Echo("same") })
            .ToQueryKernel();

        EventProjectionInclude[] includes = query.Pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .ToArray();

        Assert.Single(includes);
    }

    private sealed class WideContext
    {
        public string Label() => "ctx";
    }

    private sealed class LiteralContext
    {
        public string Echo(string value) => value;
    }

    private sealed record LiteralContextEvent(long Id) : IFilterSubject;

    private sealed class WideContextOnlyEvent : IFilterSubject
    {
        public int F00 => 0;
        public int F01 => 1;
        public int F02 => 2;
        public int F03 => 3;
        public int F04 => 4;
        public int F05 => 5;
        public int F06 => 6;
        public int F07 => 7;
        public int F08 => 8;
        public int F09 => 9;
        public int F10 => 10;
        public int F11 => 11;
        public int F12 => 12;
        public int F13 => 13;
        public int F14 => 14;
        public int F15 => 15;
        public int F16 => 16;
        public int F17 => 17;
        public int F18 => 18;
        public int F19 => 19;
        public int F20 => 20;
        public int F21 => 21;
        public int F22 => 22;
        public int F23 => 23;
        public int F24 => 24;
        public int F25 => 25;
        public int F26 => 26;
        public int F27 => 27;
        public int F28 => 28;
        public int F29 => 29;
        public int F30 => 30;
        public int F31 => 31;
        public int F32 => 32;
        public int F33 => 33;
        public int F34 => 34;
        public int F35 => 35;
        public int F36 => 36;
        public int F37 => 37;
        public int F38 => 38;
        public int F39 => 39;
        public int F40 => 40;
        public int F41 => 41;
        public int F42 => 42;
        public int F43 => 43;
        public int F44 => 44;
        public int F45 => 45;
        public int F46 => 46;
        public int F47 => 47;
        public int F48 => 48;
        public int F49 => 49;
        public int F50 => 50;
        public int F51 => 51;
        public int F52 => 52;
        public int F53 => 53;
        public int F54 => 54;
        public int F55 => 55;
        public int F56 => 56;
        public int F57 => 57;
        public int F58 => 58;
        public int F59 => 59;
        public int F60 => 60;
        public int F61 => 61;
        public int F62 => 62;
        public int F63 => 63;
        public int F64 => 64;
    }
}

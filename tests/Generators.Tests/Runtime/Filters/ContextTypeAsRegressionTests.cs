using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ContextTypeAsRegressionTests
{
    [Fact]
    public async Task ContextPredicateTypeAsNullComparisonFiltersBySubtype()
    {
        FilterSchema.RegisterValueObject(typeof(ContextEntity));
        var query = QueryKernel.For<ContextCombat, GameContext>()
            .Where(static (combat, _) => (combat.Defender as ContextMonster) != null);
        CompiledEventPipeline<GameContext> compiled = EventPipelineCompiler.Compile<GameContext>(
            typeof(ContextCombat),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? monster = await compiled.ProjectAsync(
            new ContextCombat(new ContextMonster()),
            new GameContext(),
            CancellationToken.None);
        ProjectedEvent? player = await compiled.ProjectAsync(
            new ContextCombat(new ContextPlayer()),
            new GameContext(),
            CancellationToken.None);

        Assert.NotNull(monster);
        Assert.Null(player);
    }

    private abstract record ContextEntity;

    private sealed record ContextPlayer : ContextEntity;

    private sealed record ContextMonster : ContextEntity;

    private sealed record ContextCombat(ContextEntity? Defender) : IFilterSubject;

    private sealed class GameContext;
}

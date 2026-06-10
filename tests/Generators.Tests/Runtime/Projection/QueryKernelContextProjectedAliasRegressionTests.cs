using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextProjectedAliasRegressionTests
{
    [Fact]
    public async Task ContextSelectAfterProjectedAliasUsesMostRecentAlias()
    {
        QueryKernel<PlayerNestedEvent> query = QueryKernel
            .For<PlayerNestedEvent, CombatContext>()
            .Select(new EventProjectionField(nameof(PlayerNestedEvent.Quantity), "Amount"))
            .ToQueryKernel()
            .WhereProjected(static ev => ev.Field("Amount").Integer == 2)
            .Select(new EventProjectionField(nameof(PlayerNestedEvent.Quantity), "VisibleQuantity"))
            .WithContext<PlayerNestedEvent, CombatContext>()
            .Select(static (ev, _) => new { ev.Quantity });

        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(PlayerNestedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new PlayerNestedEvent(new PlayerDetails(42), 2),
            new CombatContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(2, projected!.Field(nameof(PlayerNestedEvent.Quantity)).Integer);
    }

    [Fact]
    public async Task ReenteredContextSelectReusesExistingIncludeBinding()
    {
        Guid playerId = Guid.NewGuid();
        QueryKernel<ContextPlayerEvent> query = QueryKernel
            .For<ContextPlayerEvent, PlayerContext>()
            .Select(static (ev, ctx) => new
            {
                Name = ctx.GetPlayer(ev.PlayerId).Name,
            })
            .ToQueryKernel()
            .WithContext<ContextPlayerEvent, PlayerContext>()
            .Select(static (ev, ctx) => new
            {
                Name = ctx.GetPlayer(ev.PlayerId).Name,
            })
            .ToQueryKernel();

        EventProjectionInclude[] includes = query.Pipeline.Stages
            .First(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection
            .Includes;
        Assert.Single(includes);

        CompiledEventPipeline<PlayerContext> compiled = EventPipelineCompiler.Compile<PlayerContext>(
            typeof(ContextPlayerEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);
        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ContextPlayerEvent(playerId),
            new PlayerContext(new PlayerRecord(playerId, "Ari")),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal("Ari", projected!.Field("Name").String);
    }

    private sealed record PlayerNestedEvent(PlayerDetails Player, int Quantity) : IFilterSubject;

    private sealed record PlayerDetails(int Id);

    private sealed class CombatContext;

    private sealed record ContextPlayerEvent(Guid PlayerId) : IFilterSubject;
    private sealed record PlayerRecord(Guid Id, string Name);

    private sealed class PlayerContext(params PlayerRecord[] players)
    {
        private readonly Dictionary<Guid, PlayerRecord> _players =
            players.ToDictionary(static player => player.Id);

        public PlayerRecord GetPlayer(Guid id) =>
            _players.TryGetValue(id, out PlayerRecord? player) ? player : null!;
    }
}

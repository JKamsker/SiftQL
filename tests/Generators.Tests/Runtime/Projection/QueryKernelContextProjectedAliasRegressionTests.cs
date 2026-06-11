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

    [Fact]
    public void ReenteredContextWhereReusesIdenticalGeneratedInclude()
    {
        QueryKernel<MetricEvent, MetricContext> query = QueryKernel
            .For<MetricEvent, MetricContext>()
            .Where(static (ev, ctx) => ctx.Score(ev.Id) > 0)
            .ToQueryKernel()
            .WithContext<MetricEvent, MetricContext>()
            .Where(static (ev, ctx) => ctx.Score(ev.Id) > 0);

        EventProjectionInclude[] includes = query.Pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .ToArray();

        Assert.Single(includes);
    }

    [Fact]
    public async Task ReenteredContextWhereAllocatesDistinctGeneratedIncludeNames()
    {
        var query = QueryKernel
            .For<MetricEvent, MetricContext>()
            .Where(static (ev, ctx) => ctx.Score(ev.Id) > 0)
            .ToQueryKernel()
            .WithContext<MetricEvent, MetricContext>()
            .Where(static (ev, ctx) => ctx.Rank(ev.Id) > 0)
            .Select(nameof(MetricEvent.Id));
        CompiledEventPipeline<MetricContext> compiled = EventPipelineCompiler.Compile<MetricContext>(
            typeof(MetricEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new MetricEvent(7),
            new MetricContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(7, projected!.Field(nameof(MetricEvent.Id)).Integer);
        string[] includeNames = query.Pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .Select(static include => include.ResultName)
            .ToArray();
        Assert.Equal(2, includeNames.Length);
        Assert.False(string.Equals(includeNames[0], includeNames[1], StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, projected.Context
            .Select(static field => field.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
    }

    [Fact]
    public async Task ContextWhereAfterLaterProjectionCarriesRequiredSourceFields()
    {
        QueryKernel<MetricEvent> query = QueryKernel
            .For<MetricEvent>()
            .Select(
                new EventProjectionField(nameof(MetricEvent.Id), nameof(MetricEvent.Id)),
                new EventProjectionField(nameof(MetricEvent.Quantity), "Amount"))
            .WhereProjected(static projected => projected.Field("Amount").Integer >= 0)
            .Select(new EventProjectionField(nameof(MetricEvent.Id), "VisibleId"))
            .WithContext<MetricEvent, MetricContext>()
            .Where(static (ev, ctx) => ev.Quantity == 2 || ctx.Score(ev.Id) > 0);
        CompiledEventPipeline<MetricContext> compiled = EventPipelineCompiler.Compile<MetricContext>(
            typeof(MetricEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? sourceBranchOnly = await compiled.ProjectAsync(
            new MetricEvent(10, 2),
            new MetricContext(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            new MetricEvent(10, 1),
            new MetricContext(),
            CancellationToken.None);

        Assert.NotNull(sourceBranchOnly);
        Assert.Equal(10, sourceBranchOnly!.Field("VisibleId").Integer);
        Assert.Null(rejected);
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

    private sealed record MetricEvent(long Id, int Quantity = 1) : IFilterSubject;

    private sealed class MetricContext
    {
        public long Score(long id) => id == 10 ? 0 : id;

        public long Rank(long id) => id;
    }
}

using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextRegressionTests
{
    [Fact]
    public async Task ContextWhereThenContextSelectRunsOnPipelineAndReusesLookup()
    {
        Guid thiefId = Guid.NewGuid();
        Guid warriorId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        var context = new CombatContext(
            new Player(thiefId, Profession.Thief),
            new Player(warriorId, Profession.Warrior));
        var query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Where((ev, ctx) =>
                ev.PluginId == 7 &&
                ev.ContentId == 11 &&
                ctx.GetPlayer(ev.TargetId).Profession == Profession.Thief)
            .Select((ev, ctx) => new
            {
                ev.TargetId,
                ev.SourceId,
                ev.Duration,
                TargetProfession = ctx.GetPlayer(ev.TargetId).Profession,
            });
        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new BuffActivatedEvent(thiefId, sourceId, 7, 11, 12.5),
            context,
            CancellationToken.None);
        ProjectedEvent? wrongProfession = await compiled.ProjectAsync(
            new BuffActivatedEvent(warriorId, sourceId, 7, 11, 12.5),
            context,
            CancellationToken.None);
        ProjectedEvent? missingPlayer = await compiled.ProjectAsync(
            new BuffActivatedEvent(Guid.NewGuid(), sourceId, 7, 11, 12.5),
            context,
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Equal(thiefId, accepted!.Field(nameof(BuffActivatedEvent.TargetId)).Guid);
        Assert.Equal(sourceId, accepted.Field(nameof(BuffActivatedEvent.SourceId)).Guid);
        Assert.Equal(12.5, accepted.Field(nameof(BuffActivatedEvent.Duration)).Number);
        Assert.Equal(nameof(Profession.Thief), accepted.Field("TargetProfession").String);
        Assert.Null(wrongProfession);
        Assert.Null(missingPlayer);
        Assert.Single(query.Pipeline.Stages
            .First(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection
            .Includes);
    }

    [Fact]
    public async Task ContextSelectThenTypedWhereThenTypedSelectReturnsFinalShape()
    {
        Guid thiefId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        var context = new CombatContext(new Player(thiefId, Profession.Thief));
        var query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Select((ev, ctx) => new
            {
                ev.PluginId,
                ev.ContentId,
                tg = ev.TargetId,
                src = ev.SourceId,
                dur = ev.Duration,
                prof = ctx.GetPlayer(ev.TargetId).Profession,
            })
            .Where(ev =>
                ev.PluginId == 7 &&
                ev.ContentId == 11 &&
                ev.prof == Profession.Thief)
            .Select(ev => new
            {
                TargetIdX = ev.tg,
                SourceIdX = ev.src,
                DurationX = ev.dur,
                TargetProfessionX = ev.prof,
            });
        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new BuffActivatedEvent(thiefId, sourceId, 7, 11, 3.5),
            context,
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            new BuffActivatedEvent(thiefId, sourceId, 8, 11, 3.5),
            context,
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Null(rejected);
        Assert.Equal(4, accepted!.Fields.Length);
        Assert.Equal(thiefId, accepted.Field("TargetIdX").Guid);
        Assert.Equal(sourceId, accepted.Field("SourceIdX").Guid);
        Assert.Equal(3.5, accepted.Field("DurationX").Number);
        Assert.Equal(nameof(Profession.Thief), accepted.Field("TargetProfessionX").String);
    }

    [Fact]
    public async Task ContextSelectProjectsCapturedLocalValueAlongsideLookup()
    {
        string label = "client-label";
        Guid thiefId = Guid.NewGuid();
        var context = new CombatContext(new Player(thiefId, Profession.Thief));
        var query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Select((ev, ctx) => new
            {
                ev.TargetId,
                Label = label,
                TargetProfession = ctx.GetPlayer(ev.TargetId).Profession,
            });
        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new BuffActivatedEvent(thiefId, Guid.NewGuid(), 7, 11, 1),
            context,
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(thiefId, projected!.Field(nameof(BuffActivatedEvent.TargetId)).Guid);
        Assert.Equal(label, projected.Field("Label").String);
        Assert.Equal(nameof(Profession.Thief), projected.Field("TargetProfession").String);
        EventProjectionInclude[] includes = query.Pipeline.Stages
            .First(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection
            .Includes;
        Assert.Contains(includes, static include =>
            EventProjectionConstantIntrinsics.IsConstant(include.Intrinsic));
        Assert.Contains(includes, static include =>
            EventProjectionContextIntrinsics.TryParseMethod(include.Intrinsic, out _, out _));
    }

    [Fact]
    public async Task ContextSelectProjectsNullWhenLookupReturnsNull()
    {
        var query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Select((ev, ctx) => new
            {
                TargetProfession = ctx.GetPlayer(ev.TargetId).Profession,
            });
        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new BuffActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), 7, 11, 1),
            new CombatContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(ProjectedEventValueKind.Null, projected!.Field("TargetProfession").Kind);
    }

    private enum Profession
    {
        Thief,
        Warrior,
    }

    private sealed record BuffActivatedEvent(
        Guid TargetId,
        Guid SourceId,
        int PluginId,
        int ContentId,
        double Duration) : IFilterSubject;

    private sealed record Player(Guid Id, Profession Profession);

    private sealed class CombatContext
    {
        private readonly Dictionary<Guid, Player> _players;

        public CombatContext(params Player[] players)
        {
            _players = players.ToDictionary(static player => player.Id);
        }

        public Player GetPlayer(Guid id) =>
            _players.TryGetValue(id, out Player? player) ? player : null!;
    }
}

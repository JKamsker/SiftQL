using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextAliasRegressionTests
{
    [Fact]
    public async Task SourceOnlyContextWhereAfterAliasedProjectionKeepsSourcePath()
    {
        Guid targetId = Guid.NewGuid();
        QueryKernel<BuffActivatedEvent> query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Select(new EventProjectionField(nameof(BuffActivatedEvent.TargetId), "Target"))
            .Where((ev, _) => ev.TargetId == targetId);

        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            new BuffActivatedEvent(targetId, Guid.NewGuid(), 7),
            new CombatContext(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            new BuffActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), 7),
            new CombatContext(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Equal(targetId, accepted!.Field("Target").Guid);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task ContextSelectAfterAliasedNestedProjectionUsesAliasPrefix()
    {
        FilterSchema.RegisterValueObject(typeof(PlayerDetails));
        QueryKernel<PlayerNestedEvent> query = QueryKernel
            .For<PlayerNestedEvent, CombatContext>()
            .Select(
                new EventProjectionField(nameof(PlayerNestedEvent.Player), "P"),
                new EventProjectionField(nameof(PlayerNestedEvent.Quantity)))
            .Select((ev, _) => new { PlayerId = ev.Player.Id });

        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(PlayerNestedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new PlayerNestedEvent(new PlayerDetails(42), 2),
            new CombatContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(42, projected!.Field("PlayerId").Integer);
    }

    [Fact]
    public async Task ContextSelectorProjectsStaticMemberAsField()
    {
        QueryKernel<BuffActivatedEvent> query = QueryKernel
            .For<BuffActivatedEvent, CombatContext>()
            .Select(static (_, _) => new { Value = StaticProjectionValue });

        CompiledEventPipeline<CombatContext> compiled = EventPipelineCompiler.Compile<CombatContext>(
            typeof(BuffActivatedEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new BuffActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), 7),
            new CombatContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(42, projected!.Field("Value").Integer);
    }

    private static int StaticProjectionValue => 42;

    private sealed record BuffActivatedEvent(
        Guid TargetId,
        Guid SourceId,
        int PluginId) : IFilterSubject;

    private sealed record PlayerNestedEvent(PlayerDetails Player, int Quantity) : IFilterSubject;

    private sealed record PlayerDetails(int Id);

    private sealed class CombatContext;
}

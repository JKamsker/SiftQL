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

    private sealed record PlayerNestedEvent(PlayerDetails Player, int Quantity) : IFilterSubject;

    private sealed record PlayerDetails(int Id);

    private sealed class CombatContext;
}

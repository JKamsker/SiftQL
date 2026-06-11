using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextSubtypeRegressionTests
{
    [Fact]
    public async Task ContextSelectPassesSubtypeMemberAsSourceArgument()
    {
        FilterSchema.RegisterValueObject(typeof(SubtypeContextEntity));
        FilterSchema.RegisterValueObject(typeof(SubtypeContextPlayer));
        QueryKernel<SubtypeContextCombat> query = QueryKernel
            .For<SubtypeContextCombat, LevelContext>()
            .Select(static (combat, ctx) => new
            {
                Boosted = ctx.Boost((combat.Actor as SubtypeContextPlayer)!.Level),
            })
            .ToQueryKernel();
        CompiledEventPipeline<LevelContext> compiled = EventPipelineCompiler.Compile<LevelContext>(
            typeof(SubtypeContextCombat),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new SubtypeContextCombat(new SubtypeContextPlayer(8)),
            new LevelContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal(9, projected!.Field("Boosted").Integer);
    }

    private abstract record SubtypeContextEntity;
    private sealed record SubtypeContextPlayer(int Level) : SubtypeContextEntity;
    private sealed record SubtypeContextCombat(SubtypeContextEntity? Actor) : IFilterSubject;

    private sealed class LevelContext
    {
        public int Boost(int level) => level + 1;
    }
}

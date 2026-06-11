using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class KernelElementTypeTestAnyRegressionTests
{
    [Fact]
    public void AnyTypeTestOverPolymorphicElementsLowersToElemMatch()
    {
        FilterSchema.RegisterValueObject(typeof(EncounterActor));

        FilterExpression filter = QueryKernel.For<Encounter>()
            .Where(static encounter => encounter.Actors.Any(actor => actor is EncounterMonster))
            .Filter;

        Assert.Equal(FilterExpressionKind.ElemMatch, filter.Kind);
        Assert.Equal(nameof(Encounter.Actors), filter.Field);

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(Encounter),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new Encounter([new EncounterMonster()])));
        Assert.False(kernel.Matches(new Encounter([new EncounterPlayer()])));
    }

    private abstract record EncounterActor;
    private sealed record EncounterMonster : EncounterActor;
    private sealed record EncounterPlayer : EncounterActor;
    private sealed record Encounter(EncounterActor[] Actors) : IFilterSubject;
}

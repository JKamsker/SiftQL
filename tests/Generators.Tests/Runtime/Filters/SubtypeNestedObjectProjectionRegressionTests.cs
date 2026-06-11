using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubtypeNestedObjectProjectionRegressionTests
{
    [Fact]
    public void SubtypeObjectMemberReadExpandsNestedMembers()
    {
        FilterSchema.RegisterValueObject(typeof(NestedEntity));
        FilterSchema.RegisterValueObject(typeof(NestedPlayer));
        FilterSchema.RegisterValueObject(typeof(PlayerStats));

        var filter = QueryKernel.For<NestedCombat>()
            .Where(static combat => (combat.Actor as NestedPlayer)!.Stats!.Level > 5)
            .Filter;
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NestedCombat),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NestedCombat(new NestedPlayer(new PlayerStats(10)))));
        Assert.False(kernel.Matches(new NestedCombat(new NestedPlayer(new PlayerStats(3)))));
        Assert.False(kernel.Matches(new NestedCombat(new NestedPlayer(null))));
        Assert.False(kernel.Matches(new NestedCombat(null)));
        Assert.False(kernel.Matches(new NestedCombat(new NestedMonster())));
    }

    [Fact]
    public void SubtypeObjectMemberSupportsNestedSubtypeProjection()
    {
        FilterSchema.RegisterValueObject(typeof(NestedEntity));
        FilterSchema.RegisterValueObject(typeof(NestedPlayer));
        FilterSchema.RegisterValueObject(typeof(NestedPet));
        FilterSchema.RegisterValueObject(typeof(NestedDog));

        var filter = QueryKernel.For<NestedCombat>()
            .Where(static combat => ((combat.Actor as NestedPlayer)!.Pet as NestedDog)!.BarkLevel > 3)
            .Filter;
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NestedCombat),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NestedCombat(new NestedPlayer(Pet: new NestedDog(5)))));
        Assert.False(kernel.Matches(new NestedCombat(new NestedPlayer(Pet: new NestedDog(1)))));
        Assert.False(kernel.Matches(new NestedCombat(new NestedPlayer(Pet: new NestedCat()))));
        Assert.False(kernel.Matches(new NestedCombat(new NestedMonster())));
    }

    private abstract record NestedEntity;
    private sealed record NestedPlayer(PlayerStats? Stats = null, NestedPet? Pet = null) : NestedEntity;
    private sealed record NestedMonster : NestedEntity;
    private sealed record PlayerStats(int Level);
    private abstract record NestedPet;
    private sealed record NestedDog(int BarkLevel) : NestedPet;
    private sealed record NestedCat : NestedPet;
    private sealed record NestedCombat(NestedEntity? Actor) : IFilterSubject;
}

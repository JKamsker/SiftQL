using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubtypeNullCheckRegressionTests
{
    [Fact]
    public void AsCastNotNullMatchesOnlyTheSubtype()
    {
        FilterSchema.RegisterValueObject(typeof(NullCheckEntity));

        var filter = QueryKernel.For<NullCheckCombat>()
            .Where(static combat => (combat.Defender as NullCheckMonster) != null)
            .Filter;
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NullCheckCombat),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullCheckCombat(new NullCheckMonster())));
        Assert.True(kernel.Matches(new NullCheckCombat(new NullCheckOrc())));
        Assert.False(kernel.Matches(new NullCheckCombat(new NullCheckPlayer())));
        Assert.False(kernel.Matches(new NullCheckCombat(null)));
    }

    private abstract record NullCheckEntity;
    private sealed record NullCheckPlayer : NullCheckEntity;
    private record NullCheckMonster : NullCheckEntity;
    private sealed record NullCheckOrc : NullCheckMonster;
    private sealed record NullCheckCombat(NullCheckEntity? Defender) : IFilterSubject;
}

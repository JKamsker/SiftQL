using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class SubtypeHiddenMemberProjectionRegressionTests
{
    [Fact]
    public void SubtypeHiddenMemberReadUsesSubtypeQualifiedField()
    {
        FilterSchema.RegisterValueObject(typeof(HiddenEntity));
        FilterSchema.RegisterValueObject(typeof(HiddenPlayer));
        var filter = QueryKernel.For<HiddenCombat>()
            .Where(static combat => (combat.Actor as HiddenPlayer)!.Code > 5)
            .Filter;

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(HiddenCombat),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new HiddenCombat(new HiddenPlayer(7))));
        Assert.False(kernel.Matches(new HiddenCombat(new HiddenPlayer(3))));
        Assert.False(kernel.Matches(new HiddenCombat(new HiddenEntity("base"))));
        Assert.False(kernel.Matches(new HiddenCombat(null)));
    }

    private sealed record HiddenCombat(HiddenEntity? Actor) : IFilterSubject;

    private class HiddenEntity(string code)
    {
        public string Code { get; } = code;
    }

    private sealed class HiddenPlayer(int code) : HiddenEntity("base")
    {
        public new int Code { get; } = code;
    }
}

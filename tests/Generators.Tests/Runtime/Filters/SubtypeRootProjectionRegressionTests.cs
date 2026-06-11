using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubtypeRootProjectionRegressionTests
{
    [Fact]
    public void RootSubtypeMemberReadCompilesAndMatches()
    {
        FilterSchema.RegisterValueObject(typeof(RootEntity));
        FilterSchema.RegisterValueObject(typeof(RootPlayer));

        var filter = QueryKernel.For<RootEntity>()
            .Where(static entity => (entity as RootPlayer)!.Level > 5)
            .Filter;
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(RootEntity),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new RootPlayer(Level: 10)));
        Assert.False(kernel.Matches(new RootPlayer(Level: 3)));
        Assert.False(kernel.Matches(new RootMonster()));
    }

    private abstract record RootEntity : IFilterSubject;
    private sealed record RootPlayer(int Level = 0) : RootEntity;
    private sealed record RootMonster : RootEntity;
}

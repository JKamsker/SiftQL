using SiftQL.Compiler;
using SiftQL.Kernel;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubjectTypeMetadataShadowingRegressionTests
{
    [Fact]
    public void NestedUserSubjectTypesPropertyDoesNotShadowRuntimeTypeMetadata()
    {
        FilterSchema.RegisterValueObject(typeof(ShadowEntity));

        var filter = QueryKernel.For<ShadowCombat>()
            .Where(static combat => combat.Defender is ShadowMonster)
            .Filter;
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ShadowCombat),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ShadowCombat(new ShadowMonster([]))));
        Assert.False(kernel.Matches(new ShadowCombat(new ShadowPlayer([typeof(ShadowMonster).FullName!]))));
    }

    private abstract record ShadowEntity(string[] SubjectTypes);
    private sealed record ShadowPlayer(string[] SubjectTypes) : ShadowEntity(SubjectTypes);
    private sealed record ShadowMonster(string[] SubjectTypes) : ShadowEntity(SubjectTypes);
    private sealed record ShadowCombat(ShadowEntity? Defender) : IFilterSubject;
}

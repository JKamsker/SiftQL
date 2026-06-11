using SiftQL;
using SiftQL.Compiler;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextTypeTestRegressionTests
{
    [Fact]
    public async Task ContextIncludePredicatePreservesNestedTypeTestDiscriminator()
    {
        FilterSchema.RegisterValueObject(typeof(ContextEntity));
        QueryKernel<ContextCombat, TypeTestContext> query = QueryKernel
            .For<ContextCombat, TypeTestContext>()
            .Where(static (combat, context) =>
                combat.Defender is ContextMonster &&
                context.Enabled());
        CompiledEventPipeline<TypeTestContext> compiled = EventPipelineCompiler.Compile<TypeTestContext>(
            typeof(ContextCombat),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? matched = await compiled.ProjectAsync(
            new ContextCombat(new ContextOrc()),
            new TypeTestContext(),
            CancellationToken.None);
        ProjectedEvent? missed = await compiled.ProjectAsync(
            new ContextCombat(new ContextPlayer()),
            new TypeTestContext(),
            CancellationToken.None);

        Assert.NotNull(matched);
        Assert.Null(missed);
    }

    private abstract record ContextEntity;
    private record ContextMonster : ContextEntity;
    private sealed record ContextOrc : ContextMonster;
    private sealed record ContextPlayer : ContextEntity;
    private sealed record ContextCombat(ContextEntity? Defender) : IFilterSubject;

    private sealed class TypeTestContext
    {
        public bool Enabled() => true;
    }
}

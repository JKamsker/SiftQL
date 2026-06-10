using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextIncludeKeyRegressionTests
{
    [Fact]
    public async Task ContextIncludeKeysDoNotCollideWithStringArgumentDelimiters()
    {
        const string left = "left-value";
        const string right = "right-value";
        const string emptyGuid = "00000000-0000-0000-0000-000000000000";
        const string collision = left + ":" + emptyGuid +
            ":0|right:0:4:False:0:0:0:0:" + right;
        var query = QueryKernel.For<ContextKeyEvent, CollisionContext>()
            .Select((ev, ctx) => new
            {
                One = ctx.Echo(collision),
                Two = ctx.Echo(left, right),
            });
        CompiledEventPipeline<CollisionContext> compiled = EventPipelineCompiler.Compile<CollisionContext>(
            typeof(ContextKeyEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ContextKeyEvent(1),
            new CollisionContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal("one:" + collision, projected!.Field("One").String);
        Assert.Equal("two:" + left + ":" + right, projected.Field("Two").String);
    }

    private sealed record ContextKeyEvent(long Id) : IFilterSubject;

    private sealed class CollisionContext
    {
        public string Echo(string left) => "one:" + left;

        public string Echo(string left, string right) => "two:" + left + ":" + right;
    }
}

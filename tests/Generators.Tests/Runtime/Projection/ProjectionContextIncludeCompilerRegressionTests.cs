using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionContextIncludeCompilerRegressionTests
{
    [Fact]
    public async Task ContextIncludeArgumentsBindByNameRegardlessOfOrder()
    {
        var include = new EventProjectionInclude(
            EventProjectionContextIntrinsics.Method(nameof(PairContext.Join), ""),
            "pair",
            EventProjectionArgument.FromSourceField("right", nameof(PairEvent.Right)),
            EventProjectionArgument.FromSourceField("left", nameof(PairEvent.Left)));
        EventProjectionExpression projection = EventProjectionExpression.Default
            .WithIncludes([include]);
        CompiledProjection<PairContext> compiled = ProjectionCompiler.Compile<PairContext>(
            typeof(PairEvent),
            projection,
            ProjectionContextIncludeCompiler.Compile<PairContext>,
            ProjectionCompilerOptions.Immediate);

        ProjectedEvent projected = await compiled.ProjectAsync(
            new PairEvent("left", "right"),
            new PairContext(),
            CancellationToken.None);

        Assert.True(projected.TryGetContext("pair", out ProjectedEventValue value));
        Assert.Equal("left:right", value.String);
    }

    private sealed record PairEvent(string Left, string Right) : IFilterSubject;

    private sealed class PairContext
    {
        public string Join(string left, string right) =>
            left + ":" + right;
    }
}

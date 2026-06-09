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

    [Fact]
    public async Task LegacyContextIncludeUsesSelectedOverload()
    {
        QueryKernel<OverloadedUserEvent> query = QueryKernel
            .For<OverloadedUserEvent, OverloadedUserContext>()
            .Select(static (ev, ctx) => new
            {
                Name = ctx.User(ev.UserId).Name,
            })
            .ToQueryKernel();
        CompiledEventPipeline<OverloadedUserContext> compiled = EventPipelineCompiler.Compile<OverloadedUserContext>(
            typeof(OverloadedUserEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new OverloadedUserEvent(42),
            new OverloadedUserContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal("user-42", projected!.Field("Name").String);
    }

    private sealed record PairEvent(string Left, string Right) : IFilterSubject;

    private sealed class PairContext
    {
        public string Join(string left, string right) =>
            left + ":" + right;
    }

    private sealed record OverloadedUserEvent(long UserId) : IFilterSubject;

    private sealed record UserSnapshot(string Name);

    private sealed class OverloadedUserContext
    {
        public UserSnapshot User(long id) =>
            new("user-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public UserSnapshot User(string name) =>
            new("user-" + name);
    }
}

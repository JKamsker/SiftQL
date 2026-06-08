using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelIncludeCompositionRegressionTests
{
    [Fact]
    public async Task IncludeAfterProjectedFilterRunsInSourceProjection()
    {
        var rawInclude = new EventProjectionInclude(
            "test.limit",
            "limit",
            [new EventProjectionArgument("limit", FilterValue.From(3L))]);

        EventPipelineExpression pipeline = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Include(rawInclude)
            .Pipeline;

        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileLimitInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 7, 100, 2),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.True(projected!.TryGetContext("limit", out ProjectedEventValue limit));
        Assert.Equal(3, limit.Integer);
    }

    private static CompiledProjection<object>.IncludeProjector CompileLimitInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        int limit = ProjectionIncludeArguments.RequiredInt(include, "limit");
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(limit)));
    }
}

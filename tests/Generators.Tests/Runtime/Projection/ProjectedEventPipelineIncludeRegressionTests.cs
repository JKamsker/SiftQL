using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedEventPipelineIncludeRegressionTests
{
    [Fact]
    public async Task ProjectedEventPipelineFirstProjectionAllowsIncludes()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.marker", "Marker")]));
        CompiledEventPipeline<string> compiled = EventPipelineCompiler.Compile<string>(
            typeof(ProjectedEvent),
            pipeline,
            CompileMarkerInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ProjectedEvent { EventType = "Projected", EventName = "Projected" },
            "ok",
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal("ok", projected!.ContextValue("Marker").String);
    }

    private static CompiledProjection<string>.IncludeProjector CompileMarkerInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<string>.IncludeProjector(
            include.ResultName,
            static (_, context, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(context)));
    }
}

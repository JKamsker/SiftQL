using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionCompilerKeyRegressionTests
{
    [Fact]
    public void ProjectionKeysSeparateEqualFieldShapesAcrossSubjectTypes()
    {
        EventProjectionExpression projection = EventProjectionExpression.Select(nameof(FirstValueEvent.Value));
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(FirstValueEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(SecondValueEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        var accumulator = new ProjectionMatchAccumulator<CompiledProjection<object>>();

        accumulator.Add("first", first.Key, first);
        accumulator.Add("second", second.Key, second);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, accumulator.GroupCount);
    }

    [Fact]
    public void ProjectionAndPipelineKeysSeparateClosedGenericIncludeCompilers()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.marker", "Marker"),
        ]);
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            CompileMarker<FirstMarker>,
            ProjectionCompilerOptions.Immediate);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            CompileMarker<SecondMarker>,
            ProjectionCompilerOptions.Immediate);
        EventPipelineExpression pipeline = EventPipelineExpression.Default.AppendProjection(projection);
        CompiledEventPipeline<object> firstPipeline = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileMarker<FirstMarker>,
            EventPipelineCompilerOptions.Immediate);
        CompiledEventPipeline<object> secondPipeline = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileMarker<SecondMarker>,
            EventPipelineCompilerOptions.Immediate);

        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(firstPipeline.Key, secondPipeline.Key);
    }

    private static CompiledProjection<object>.IncludeProjector CompileMarker<TMarker>(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(
                ProjectedEventValue.FromScalar(typeof(TMarker).Name)));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record FirstValueEvent(int Value) : IFilterSubject;

    private sealed record SecondValueEvent(int Value) : IFilterSubject;

    private sealed class FirstMarker;

    private sealed class SecondMarker;
}

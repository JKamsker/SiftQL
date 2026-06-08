using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionIncludeCompilerKeyRegressionTests
{
    [Fact]
    public void ProjectionKeysSeparateDifferentIncludeCompilers()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.value", "Value", []),
        ]);
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            new IncludeCompiler(3).Compile,
            ProjectionCompilerOptions.Immediate);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            new IncludeCompiler(5).Compile,
            ProjectionCompilerOptions.Immediate);
        var accumulator = new ProjectionMatchAccumulator<CompiledProjection<object>>();

        accumulator.Add("first", first.Key, first);
        accumulator.Add("second", second.Key, second);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, accumulator.GroupCount);
    }

    private sealed class IncludeCompiler(int value)
    {
        public CompiledProjection<object>.IncludeProjector Compile(
            FilterSchema schema,
            EventProjectionInclude include)
        {
            _ = schema;
            return new CompiledProjection<object>.IncludeProjector(
                include.ResultName,
                (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(value)));
        }
    }
}

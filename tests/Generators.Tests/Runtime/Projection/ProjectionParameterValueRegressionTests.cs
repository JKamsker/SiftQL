using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionParameterValueRegressionTests
{
    [Fact]
    public void ProjectionIncludesRejectDuplicateParameterKeysWithSignedZeroValues()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.zero",
                "zero",
                [
                    new EventProjectionArgument(
                        "first",
                        FilterValue.From(0.0D) with { ParameterKey = "p0" }),
                    new EventProjectionArgument(
                        "second",
                        FilterValue.From(-0.0D) with { ParameterKey = "p0" }),
                ]),
        ]);

        FilterValidationException exception = Assert.Throws<FilterValidationException>(() =>
            ProjectionCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                projection,
                CompileNoopInclude));

        Assert.Contains("p0", exception.Message, StringComparison.Ordinal);
    }

    private static CompiledProjection<object>.IncludeProjector CompileNoopInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.Null));
    }
}

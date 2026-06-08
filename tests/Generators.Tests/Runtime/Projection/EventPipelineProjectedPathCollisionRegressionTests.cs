using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineProjectedPathCollisionRegressionTests
{
    [Fact]
    public void ImplicitProjectionRejectsFieldAndContextOutputNameCollision()
    {
        var pipeline = EventPipelineExpression.Default.AppendFilter(
            FilterExpression.And(
                FilterExpression.Compare(
                    ProjectedEventPaths.Field("tag"),
                    FilterOperator.Equal,
                    FilterValue.From("field")),
                FilterExpression.Compare(
                    ProjectedEventPaths.Context("tag"),
                    FilterOperator.Equal,
                    FilterValue.From("context"))));

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ProjectedEvent),
                pipeline,
                ProjectionRuntimeTestSupport.RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }
}

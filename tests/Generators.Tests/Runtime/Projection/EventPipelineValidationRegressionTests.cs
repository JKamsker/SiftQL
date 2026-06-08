using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineValidationRegressionTests
{
    [Fact]
    public void ProjectedProjectionStageWithNullFieldsThrowsValidationException()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(PipelineEvent.EventId)))
            .AppendProjection(EventProjectionExpression.Default with { Fields = null! });

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(PipelineEvent),
                pipeline,
                RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record PipelineEvent(Guid EventId) : IFilterSubject;
}

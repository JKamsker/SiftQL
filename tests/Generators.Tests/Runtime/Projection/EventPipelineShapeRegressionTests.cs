using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineShapeRegressionTests
{
    [Fact]
    public async Task PipelineCacheSeparatesProjectionIncludeNegativeZeroArguments()
    {
        CompiledEventPipeline<object> positive = EventPipelineCompiler.Compile<object>(
            typeof(ZeroIncludeEvent),
            SignPipeline(0.0D),
            CompileSignInclude,
            EventPipelineCompilerOptions.Immediate);
        CompiledEventPipeline<object> negative = EventPipelineCompiler.Compile<object>(
            typeof(ZeroIncludeEvent),
            SignPipeline(-0.0D),
            CompileSignInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await negative.ProjectAsync(
            new ZeroIncludeEvent(1),
            new object(),
            CancellationToken.None);

        Assert.NotSame(positive, negative);
        Assert.Equal("negative", projected!.ContextValue("sign").String);
    }

    [Fact]
    public void NullPipelineStagesThrowsValidationException()
    {
        var pipeline = new EventPipelineExpression { Stages = null! };

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ZeroIncludeEvent),
                pipeline,
                RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    [Fact]
    public void NullPipelineStageThrowsValidationException()
    {
        var pipeline = new EventPipelineExpression { Stages = [null!] };

        Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ZeroIncludeEvent),
                pipeline,
                RejectInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    private static EventPipelineExpression SignPipeline(double value) =>
        EventPipelineExpression.Default.AppendProjection(
            EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude(
                    "test.sign",
                    "sign",
                    [new EventProjectionArgument("value", FilterValue.From(value))]),
            ]));

    private static CompiledProjection<object>.IncludeProjector CompileSignInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        double value = ProjectionIncludeArguments.RequiredDouble(include, "value");
        string sign = BitConverter.DoubleToInt64Bits(value) < 0 ? "negative" : "positive";
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(sign)));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record ZeroIncludeEvent(long ItemId) : IFilterSubject;
}

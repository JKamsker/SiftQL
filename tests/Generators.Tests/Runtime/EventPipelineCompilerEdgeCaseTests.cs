using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineCompilerEdgeCaseTests
{
    [Fact]
    public void SourceFilter_ExtractsFiltersBeforeProjection()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId), FilterOperator.Equal, FilterValue.From(10L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default)
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)),
                FilterOperator.Equal,
                FilterValue.From(10L)));

        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(pipeline);

        Assert.NotEqual(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void SourceFilter_NullPipeline_ReturnsAny()
    {
        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(null);
        Assert.Equal(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void ProjectionDispatchPipeline_NullPipeline_ReturnsDefault()
    {
        EventPipelineExpression result = EventPipelineCompiler.ProjectionDispatchPipeline(null);
        Assert.NotNull(result);
    }

    [Fact]
    public void ProjectionDispatchPipeline_PreservesPostProjectionStages()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId), FilterOperator.Equal, FilterValue.From(10L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default);

        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);

        Assert.True(dispatch.Stages.Length <= pipeline.Stages.Length);
    }

    [Fact]
    public void RejectProjectedInclude_ThrowsFilterValidationException()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.intrinsic", "tag")]))
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.intrinsic", "tag2")]));

        var ex = Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                CompileSimpleInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    [Fact]
    public void PipelineWithParameterizedFilter_BypassesCache()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        var first = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var second = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        Assert.NotSame(first, second);
    }

    private static CompiledProjection<object>.IncludeProjector CompileSimpleInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("included")));
    }
}

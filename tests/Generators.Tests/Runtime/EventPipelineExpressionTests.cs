using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class EventPipelineExpressionTests
{
    [Fact]
    public void AppendFilter_AnyFilter_ReturnsSame()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendFilter(FilterExpression.Any);
        Assert.Same(pipeline, result);
    }

    [Fact]
    public void AppendSourceFilter_AnyFilter_ReturnsSame()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendSourceFilter(FilterExpression.Any);
        Assert.Same(pipeline, result);
    }

    [Fact]
    public void AppendSourceFilter_NoProjection_AppendsLikeNormalFilter()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendSourceFilter(filter);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Filter, result.Stages[0].Kind);
    }

    [Fact]
    public void AppendSourceFilter_WithProjection_InsertsBeforeProjection()
    {
        var filter1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var filter2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default);

        EventPipelineExpression result = pipeline.AppendSourceFilter(filter2);

        Assert.Equal(2, result.Stages.Length);
        Assert.Equal(EventPipelineStageKind.Filter, result.Stages[0].Kind);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[1].Kind);
    }

    [Fact]
    public void AppendOrMergeLastProjection_NoExistingProjection_Appends()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(projection);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[0].Kind);
    }

    [Fact]
    public void AppendOrMergeLastProjection_ExistingProjection_MergesFields()
    {
        var proj1 = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        var proj2 = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(proj1);

        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(proj2);

        Assert.Single(result.Stages);
        Assert.Equal(2, result.Stages[0].Projection.Fields.Length);
    }

    [Fact]
    public void AppendOrMergeLastProjection_LastStageIsFilter_Appends()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default)
            .AppendFilter(filter);

        var proj = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(proj);

        Assert.Equal(3, result.Stages.Length);
    }

    [Fact]
    public void From_NullFilterAndProjection_ReturnsDefault()
    {
        EventPipelineExpression result = EventPipelineExpression.From(null, null);
        Assert.True(result.IsDefault);
    }

    [Fact]
    public void From_FilterAndProjection_CreatesPipeline()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = EventPipelineExpression.From(filter, projection);
        Assert.Equal(2, result.Stages.Length);
    }

    [Fact]
    public void From_AnyFilter_SkipsFilterStage()
    {
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = EventPipelineExpression.From(FilterExpression.Any, projection);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[0].Kind);
    }

    [Fact]
    public void IsDefault_True_ForNewPipeline()
    {
        Assert.True(EventPipelineExpression.Default.IsDefault);
    }

    [Fact]
    public void HasProjection_False_ForFilterOnly()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L)));
        Assert.False(pipeline.HasProjection);
    }
}

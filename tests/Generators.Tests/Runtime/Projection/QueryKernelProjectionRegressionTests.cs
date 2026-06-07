using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelProjectionRegressionTests
{
    [Fact]
    public void SelectorProjectionAfterProjectedFilterReadsProjectedFields()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(static ev => ev.ItemId, static ev => ev.Quantity)
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(static (ev, _) => new { ev.Quantity });

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Field(nameof(ItemUsedEvent.Quantity)), field.Path);
    }

    [Fact]
    public void ExplicitProjectedPathIsNotDoublePrefixed()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)));

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)), field.Path);
    }

    [Fact]
    public void ExplicitProjectedContextPathIsNotRebased()
    {
        QueryKernel<ItemUsedEvent> kernel = QueryKernel.For<ItemUsedEvent>()
            .Select(nameof(ItemUsedEvent.ItemId))
            .WhereProjected(static projected =>
                projected.Field(nameof(ItemUsedEvent.ItemId)).Integer == 100)
            .Select(new EventProjectionField(ProjectedEventPaths.Context("tag"), "tag"));

        EventProjectionField field = LastProjection(kernel).Fields.Single();

        Assert.Equal(ProjectedEventPaths.Context("tag"), field.Path);
    }

    [Fact]
    public void UnsupportedProjectedValueMemberIsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<ItemUsedEvent>()
                .Select(nameof(ItemUsedEvent.ItemId))
                .WhereProjected(static projected =>
                    projected.Field(nameof(ItemUsedEvent.ItemId)).Kind ==
                    ProjectedEventValueKind.Integer));
    }

    private static EventProjectionExpression LastProjection(QueryKernel<ItemUsedEvent> kernel) =>
        kernel.Pipeline.Stages
            .Last(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .Projection;
}

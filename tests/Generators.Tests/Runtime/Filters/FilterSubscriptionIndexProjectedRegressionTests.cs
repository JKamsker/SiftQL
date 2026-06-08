using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexProjectedRegressionTests
{
    [Fact]
    public void UntypedIndexAddsProjectedEventDynamicFilters()
    {
        CompiledEventPipeline<object> compiled = CompileProjectedItemFilter();
        var index = new FilterSubscriptionIndex<string>(typeof(ProjectedEvent));

        index.Add("projected", compiled.IndexFilter);

        Assert.Equal(["projected"], index.SnapshotMatches(ProjectedItem(100)));
        Assert.Empty(index.SnapshotMatches(ProjectedItem(101)));
    }

    [Fact]
    public void TypedIndexAddsProjectedEventDynamicFilters()
    {
        CompiledEventPipeline<object> compiled = CompileProjectedItemFilter();
        var index = new TypedFilterSubscriptionIndex<string, ProjectedEvent>();

        index.Add("projected", compiled.IndexFilter);

        Assert.Equal(["projected"], index.SnapshotMatches(ProjectedItem(100)));
        Assert.Empty(index.SnapshotMatches(ProjectedItem(101)));
    }

    [Fact]
    public void UntypedIndexMatchesProjectedEventMetadataAliases()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(ProjectedEvent));

        AddMetadataFilters(index);

        Assert.Equal(["name", "type"], index.SnapshotMatches(ProjectedInventory()));
        Assert.Empty(index.SnapshotMatches(new ProjectedEvent
        {
            EventType = "Plugin.Events.OtherEvent",
            EventName = "OtherEvent",
        }));
    }

    [Fact]
    public void TypedIndexMatchesProjectedEventMetadataAliases()
    {
        var index = new TypedFilterSubscriptionIndex<string, ProjectedEvent>();

        AddMetadataFilters(index);

        Assert.Equal(["name", "type"], index.SnapshotMatches(ProjectedInventory()));
        Assert.Empty(index.SnapshotMatches(new ProjectedEvent
        {
            EventType = "Plugin.Events.OtherEvent",
            EventName = "OtherEvent",
        }));
    }

    private static CompiledEventPipeline<object> CompileProjectedItemFilter()
    {
        QueryKernel<ProjectedEvent> query = QueryKernel.For<ProjectedEvent>()
            .WhereProjected(static ev => ev.Field("ItemId").Integer == 100);
        return EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            query.Pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
    }

    private static ProjectedEvent ProjectedItem(long itemId) =>
        new()
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields =
            [
                new ProjectedEventField("ItemId", ProjectedEventValue.FromScalar(itemId)),
            ],
        };

    private static void AddMetadataFilters(FilterSubscriptionIndex<string> index)
    {
        index.Add("name", SubjectNameFilter());
        index.Add("type", SubjectTypeFilter());
    }

    private static void AddMetadataFilters(TypedFilterSubscriptionIndex<string, ProjectedEvent> index)
    {
        index.Add("name", SubjectNameFilter());
        index.Add("type", SubjectTypeFilter());
    }

    private static FilterExpression SubjectNameFilter() =>
        FilterExpression.Compare(
            "subjectName",
            FilterOperator.Equal,
            FilterValue.From("InventoryChangedEvent"));

    private static FilterExpression SubjectTypeFilter() =>
        FilterExpression.Compare(
            "subjectType",
            FilterOperator.Equal,
            FilterValue.From("Plugin.Events.InventoryChangedEvent"));

    private static ProjectedEvent ProjectedInventory() =>
        new()
        {
            EventType = "Plugin.Events.InventoryChangedEvent",
            EventName = "InventoryChangedEvent",
        };

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }
}

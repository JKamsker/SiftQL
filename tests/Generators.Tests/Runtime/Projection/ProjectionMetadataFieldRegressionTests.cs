using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionMetadataFieldRegressionTests
{
    [Fact]
    public async Task DefaultProjectionKeepsRealEventTypeAndEventNameFields()
    {
        CompiledProjection<object?> projection = ProjectionCompiler.Compile<object?>(
            typeof(EventMetadataNamedProjectionEvent),
            EventProjectionExpression.Default,
            RejectInclude);

        ProjectedEvent projected = await projection.ProjectAsync(
            new EventMetadataNamedProjectionEvent("payload-type", "payload-name", 7),
            null,
            CancellationToken.None);

        Assert.Equal("payload-type", projected.Field(nameof(EventMetadataNamedProjectionEvent.EventType)).String);
        Assert.Equal("payload-name", projected.Field(nameof(EventMetadataNamedProjectionEvent.EventName)).String);
        Assert.Equal(7, projected.Field(nameof(EventMetadataNamedProjectionEvent.Value)).Integer);
    }

    private static CompiledProjection<object?>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record EventMetadataNamedProjectionEvent(
        string EventType,
        string EventName,
        int Value);
}

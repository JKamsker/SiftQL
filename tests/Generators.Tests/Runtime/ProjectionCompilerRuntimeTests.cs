using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

internal static class ProjectionCompilerRuntimeTests
{
    public static void RunAll()
    {
        DefaultProjectionSkipsVirtualMetadataFields();
    }

    private static void DefaultProjectionSkipsVirtualMetadataFields()
    {
        var projection = ProjectionCompiler.Compile<object?>(
            typeof(DefaultProjectionEvent),
            EventProjectionExpression.Default,
            RejectInclude);
        var projected = projection.ProjectAsync(
                new DefaultProjectionEvent(Guid.NewGuid(), 42, 125),
                null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        string fieldNames = string.Join(",", projected.Fields.Select(static field => field.Name));
        AssertEx.Equal(
            "CharacterId,Damage,EventId",
            fieldNames,
            "default projection emits scalar event fields only");
        AssertEx.True(
            !projected.TryGetField("subjectType", out _) &&
            !projected.TryGetField("subjectName", out _),
            "default projection skips virtual filter metadata fields");
    }

    private static CompiledProjection<object?>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record DefaultProjectionEvent(
        Guid EventId,
        long CharacterId,
        int Damage);
}

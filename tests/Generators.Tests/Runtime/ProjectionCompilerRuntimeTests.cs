using SiftQL;
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
        SelectingPlayerKeepsItems();
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

    private static void SelectingPlayerKeepsItems()
    {
        FilterSchema.RegisterValueObject<PlayerProjection>();
        var kernel = QueryKernel
            .For<PlayerSelectedProjectionEvent>()
            .Select(static ev => ev.Player);
        var projection = ProjectionCompiler.Compile<object?>(
            typeof(PlayerSelectedProjectionEvent),
            kernel.Projection,
            RejectInclude);
        var projected = projection.ProjectAsync(
                new PlayerSelectedProjectionEvent(
                    Guid.NewGuid(),
                    new PlayerProjection(
                        1001,
                        "Aster",
                        [
                            new PlayerItemProjection(10, "Potion", 4),
                            new PlayerItemProjection(11, "Ore", 2),
                        ])),
                null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.True(projected.TryGetField("Player", out ProjectedEventValue player), "selected player emitted");
        AssertEx.Equal(ProjectedEventValueKind.Object, player.Kind, "selected player is projected as an object");
        AssertEx.Equal(1001L, RequiredField(player, "Id").Integer, "selected player keeps id");

        ProjectedEventValue items = RequiredField(player, "Items");
        AssertEx.Equal(ProjectedEventValueKind.Array, items.Kind, "selected player keeps items");
        AssertEx.Equal(2, items.Values.Length, "selected player keeps every item");
        AssertEx.Equal("Potion", RequiredField(items.Values[0], "Name").String, "first item keeps name");
        AssertEx.Equal(4L, RequiredField(items.Values[0], "Quantity").Integer, "first item keeps quantity");
    }

    private static ProjectedEventValue RequiredField(ProjectedEventValue value, string name)
    {
        for (int i = 0; i < value.Fields.Length; i++)
        {
            if (string.Equals(value.Fields[i].Name, name, StringComparison.Ordinal))
                return value.Fields[i].Value;
        }

        throw new InvalidOperationException($"Projected object is missing field '{name}'.");
    }

    private sealed record PlayerSelectedProjectionEvent(Guid EventId, PlayerProjection Player);

    private sealed record PlayerProjection(
        long Id,
        string Name,
        PlayerItemProjection[] Items);

    private sealed record PlayerItemProjection(
        long ItemId,
        string Name,
        int Quantity);
}

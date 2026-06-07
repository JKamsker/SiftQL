using MessagePack;
using MessagePack.Resolvers;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionCompilerRuntimeTests
{
    [Fact]
    public async Task DefaultProjectionSkipsVirtualMetadataFields()
    {
        var projection = ProjectionCompiler.Compile<object?>(
            typeof(DefaultProjectionEvent),
            EventProjectionExpression.Default,
            RejectInclude);
        var projected = await projection.ProjectAsync(
            new DefaultProjectionEvent(Guid.NewGuid(), 42, 125),
            null,
            CancellationToken.None);

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

    [Fact]
    public async Task ScalarProjectedPayloadMatchesMaterializedProjection()
    {
        var item = new PayloadProjectionEvent(
            Guid.NewGuid(),
            42,
            true,
            "inventory",
            OptionalScore: null);
        var projection = ProjectionCompiler.Compile<object?>(
            typeof(PayloadProjectionEvent),
            EventProjectionExpression.Select(
                nameof(PayloadProjectionEvent.EventId),
                nameof(PayloadProjectionEvent.CharacterId),
                nameof(PayloadProjectionEvent.Accepted),
                nameof(PayloadProjectionEvent.Name),
                nameof(PayloadProjectionEvent.OptionalScore)),
            RejectInclude);
        MessagePackSerializerOptions options =
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        ProjectedEvent materialized = await projection.ProjectAsync(
            item,
            null,
            CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            item,
            null,
            options,
            CancellationToken.None);
        ProjectedEvent roundTripped = MessagePackSerializer.Deserialize<ProjectedEvent>(
            payload,
            options);

        AssertEx.Equal(materialized.EventType, roundTripped.EventType, "payload keeps event type");
        AssertEx.Equal(materialized.EventName, roundTripped.EventName, "payload keeps event name");
        AssertProjectedField(roundTripped, nameof(PayloadProjectionEvent.EventId), ProjectedEventValueKind.Guid);
        AssertProjectedField(roundTripped, nameof(PayloadProjectionEvent.CharacterId), ProjectedEventValueKind.Integer);
        AssertProjectedField(roundTripped, nameof(PayloadProjectionEvent.Accepted), ProjectedEventValueKind.Boolean);
        AssertProjectedField(roundTripped, nameof(PayloadProjectionEvent.Name), ProjectedEventValueKind.String);
        AssertProjectedField(roundTripped, nameof(PayloadProjectionEvent.OptionalScore), ProjectedEventValueKind.Null);
    }

    [Fact]
    public async Task SelectingPlayerKeepsItems()
    {
        FilterSchema.RegisterValueObject<PlayerProjection>();
        var kernel = QueryKernel
            .For<PlayerSelectedProjectionEvent>()
            .Select(static ev => ev.Player);
        var projection = ProjectionCompiler.Compile<object?>(
            typeof(PlayerSelectedProjectionEvent),
            kernel.Projection,
            RejectInclude);
        var projected = await projection.ProjectAsync(
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
            CancellationToken.None);

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

    private static void AssertProjectedField(
        ProjectedEvent projected,
        string name,
        ProjectedEventValueKind kind)
    {
        AssertEx.True(projected.TryGetField(name, out ProjectedEventValue value), $"payload contains {name}");
        AssertEx.Equal(kind, value.Kind, $"payload field {name} kind");
    }

    private sealed record PayloadProjectionEvent(
        Guid EventId,
        long CharacterId,
        bool Accepted,
        string Name,
        long? OptionalScore);

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

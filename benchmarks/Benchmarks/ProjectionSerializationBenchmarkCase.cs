using System.Buffers;
// removed: game-specific value types
using SiftQL;
using SiftQL.Projected;
using MessagePack;
using MessagePack.Resolvers;

namespace SiftQL.Benchmarks;

internal sealed class ProjectedPayloadSerializationCase : IBenchmarkCase
{
    private static readonly MessagePackSerializerOptions s_messagePackOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
    private static readonly string s_eventType = typeof(ScalarArrayEvent).FullName ?? nameof(ScalarArrayEvent);
    private readonly ScalarArrayEvent _event = new(
        Guid.NewGuid(),
        CharacterId: 10,
        MapId: 42,
        Damage: 1_500,
        Accepted: true,
        new SkillRef(777, "FireBlast"),
        [111, 222, 999, 333],
        ["quest", "pvp", "siege"]);

    public ProjectedPayloadSerializationCase() =>
        VerifyFusedPayload();

    public string Category => "Serialization";
    public string Name => "projected event payload";
    public int Iterations => 200_000;

    public void Manual(int iterations)
    {
        var item = _event;
        for (int i = 0; i < iterations; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            MessagePackSerializer.Serialize(
                buffer,
                CreateProjectedEvent(item),
                s_messagePackOptions);
            BenchmarkSink.Consume(buffer.WrittenCount);
        }
    }

    public void Engine(int iterations)
    {
        var item = _event;
        for (int i = 0; i < iterations; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            FusedProjectedPayloadWriter.Write(buffer, item, s_eventType);
            BenchmarkSink.Consume(buffer.WrittenCount);
        }
    }

    private static ProjectedEvent CreateProjectedEvent(ScalarArrayEvent item) =>
        new()
        {
            EventType = s_eventType,
            EventName = nameof(ScalarArrayEvent),
            Fields =
            [
                ProjectedValues.Field(nameof(ScalarArrayEvent.CharacterId), ProjectedValues.Integer(item.CharacterId)),
                ProjectedValues.Field(nameof(ScalarArrayEvent.Damage), ProjectedValues.Integer(item.Damage)),
                ProjectedValues.Field("SkillId", ProjectedValues.Integer(item.Skill.Id)),
            ],
            Context = [ProjectedValues.Field("nearby", ProjectedValues.NearbyPlayers())],
        };

    private void VerifyFusedPayload()
    {
        var buffer = new ArrayBufferWriter<byte>();
        FusedProjectedPayloadWriter.Write(buffer, _event, s_eventType);
        var roundTripped = MessagePackSerializer.Deserialize<ProjectedEvent>(
            buffer.WrittenMemory,
            s_messagePackOptions);

        if (!roundTripped.TryGetField("SkillId", out ProjectedEventValue skillId) ||
            skillId.Kind != ProjectedEventValueKind.Integer ||
            skillId.Integer != _event.Skill.Id ||
            !roundTripped.TryGetContext("nearby", out ProjectedEventValue nearby) ||
            nearby.Kind != ProjectedEventValueKind.Array ||
            nearby.Values.Length != 3)
        {
            throw new InvalidOperationException("Fused projected event payload is not MessagePack-compatible.");
        }
    }
}

internal static class FusedProjectedPayloadWriter
{
    private const int ProjectedEventPropertyCount = 4;
    private const int ProjectedEventFieldPropertyCount = 2;
    private const int ProjectedEventValuePropertyCount = 8;

    public static void Write(ArrayBufferWriter<byte> buffer, ScalarArrayEvent item, string eventType)
    {
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(ProjectedEventPropertyCount);
        writer.Write(nameof(ProjectedEvent.EventType));
        writer.Write(eventType);
        writer.Write(nameof(ProjectedEvent.EventName));
        writer.Write(nameof(ScalarArrayEvent));
        writer.Write(nameof(ProjectedEvent.Fields));
        WriteFields(ref writer, item);
        writer.Write(nameof(ProjectedEvent.Context));
        writer.WriteArrayHeader(1);
        WriteNearbyField(ref writer);
        writer.Flush();
    }

    private static void WriteFields(ref MessagePackWriter writer, ScalarArrayEvent item)
    {
        writer.WriteArrayHeader(3);
        WriteIntegerField(ref writer, nameof(ScalarArrayEvent.CharacterId), item.CharacterId);
        WriteIntegerField(ref writer, nameof(ScalarArrayEvent.Damage), item.Damage);
        WriteIntegerField(ref writer, "SkillId", item.Skill.Id);
    }

    private static void WriteNearbyField(ref MessagePackWriter writer)
    {
        WriteFieldHeader(ref writer, "nearby");
        WriteArrayValueHeader(ref writer, 3);
        WritePlayerValue(ref writer, 101, "Alpha", 4);
        WritePlayerValue(ref writer, 102, "Beta", 8);
        WritePlayerValue(ref writer, 103, "Gamma", 12);
        WriteEmptyFields(ref writer);
    }

    private static void WritePlayerValue(
        ref MessagePackWriter writer,
        long characterId,
        string name,
        long distance)
    {
        WriteObjectValueHeader(ref writer, 3);
        WriteIntegerField(ref writer, "CharacterId", characterId);
        WriteStringField(ref writer, "Name", name);
        WriteIntegerField(ref writer, "Distance", distance);
    }

    private static void WriteIntegerField(ref MessagePackWriter writer, string name, long value)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValueHeader(ref writer, ProjectedEventValueKind.Integer);
        writer.Write(nameof(ProjectedEventValue.Integer));
        writer.Write(value);
        WriteScalarValueTail(ref writer, stringValue: null);
    }

    private static void WriteStringField(ref MessagePackWriter writer, string name, string value)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValueHeader(ref writer, ProjectedEventValueKind.String);
        writer.Write(nameof(ProjectedEventValue.Integer));
        writer.Write(0L);
        WriteScalarValueTail(ref writer, value);
    }

    private static void WriteFieldHeader(ref MessagePackWriter writer, string name)
    {
        writer.WriteMapHeader(ProjectedEventFieldPropertyCount);
        writer.Write(nameof(ProjectedEventField.Name));
        writer.Write(name);
        writer.Write(nameof(ProjectedEventField.Value));
    }

    private static void WriteScalarValueHeader(ref MessagePackWriter writer, ProjectedEventValueKind kind)
    {
        writer.WriteMapHeader(ProjectedEventValuePropertyCount);
        writer.Write(nameof(ProjectedEventValue.Kind));
        writer.Write((int)kind);
        writer.Write(nameof(ProjectedEventValue.Boolean));
        writer.Write(false);
    }

    private static void WriteScalarValueTail(ref MessagePackWriter writer, string? stringValue)
    {
        writer.Write(nameof(ProjectedEventValue.Number));
        writer.Write(0d);
        writer.Write(nameof(ProjectedEventValue.String));
        if (stringValue is null)
            writer.WriteNil();
        else
            writer.Write(stringValue);

        WriteEmptyGuid(ref writer);
        WriteEmptyValues(ref writer);
        WriteEmptyFields(ref writer);
    }

    private static void WriteArrayValueHeader(ref MessagePackWriter writer, int valueCount)
    {
        WriteCompositeValueHeader(ref writer, ProjectedEventValueKind.Array);
        writer.Write(nameof(ProjectedEventValue.Values));
        writer.WriteArrayHeader(valueCount);
    }

    private static void WriteObjectValueHeader(ref MessagePackWriter writer, int fieldCount)
    {
        WriteCompositeValueHeader(ref writer, ProjectedEventValueKind.Object);
        WriteEmptyValues(ref writer);
        writer.Write(nameof(ProjectedEventValue.Fields));
        writer.WriteArrayHeader(fieldCount);
    }

    private static void WriteCompositeValueHeader(ref MessagePackWriter writer, ProjectedEventValueKind kind)
    {
        writer.WriteMapHeader(ProjectedEventValuePropertyCount);
        writer.Write(nameof(ProjectedEventValue.Kind));
        writer.Write((int)kind);
        writer.Write(nameof(ProjectedEventValue.Boolean));
        writer.Write(false);
        writer.Write(nameof(ProjectedEventValue.Integer));
        writer.Write(0L);
        writer.Write(nameof(ProjectedEventValue.Number));
        writer.Write(0d);
        writer.Write(nameof(ProjectedEventValue.String));
        writer.WriteNil();
        WriteEmptyGuid(ref writer);
    }

    private static void WriteEmptyGuid(ref MessagePackWriter writer)
    {
        writer.Write(nameof(ProjectedEventValue.Guid));
        MessagePackSerializer.Serialize(ref writer, Guid.Empty, MessagePackSerializerOptions.Standard);
    }

    private static void WriteEmptyValues(ref MessagePackWriter writer)
    {
        writer.Write(nameof(ProjectedEventValue.Values));
        writer.WriteArrayHeader(0);
    }

    private static void WriteEmptyFields(ref MessagePackWriter writer)
    {
        writer.Write(nameof(ProjectedEventValue.Fields));
        writer.WriteArrayHeader(0);
    }
}

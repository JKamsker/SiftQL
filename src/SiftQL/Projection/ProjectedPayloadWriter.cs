using System.Buffers;
using SiftQL;
using SiftQL.Projected;
using MessagePack;

namespace SiftQL.Projection;

internal static class ProjectedPayloadWriter
{
    private const int ProjectedEventPropertyCount = 4;
    private const int ProjectedEventFieldPropertyCount = 2;

    public static ReadOnlyMemory<byte> Write<TContext>(
        string eventType,
        string eventName,
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        object subject,
        IReadOnlyList<ProjectedEventField>? context,
        MessagePackSerializerOptions options)
    {
        var buffer = new ArrayBufferWriter<byte>(EstimateCapacity(eventType, eventName, fields.Count, context?.Count ?? 0));
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(ProjectedEventPropertyCount);
        writer.Write(nameof(ProjectedEvent.EventType));
        writer.Write(eventType);
        writer.Write(nameof(ProjectedEvent.EventName));
        writer.Write(eventName);
        writer.Write(nameof(ProjectedEvent.Fields));
        WriteFields(ref writer, fields, subject, options);
        writer.Write(nameof(ProjectedEvent.Context));
        WriteContext(ref writer, context, options);
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteFields<TContext>(
        ref MessagePackWriter writer,
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        object subject,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(fields.Count);
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            WriteField(ref writer, field.Name, field.ProjectValue(subject), options);
        }
    }

    private static void WriteContext(
        ref MessagePackWriter writer,
        IReadOnlyList<ProjectedEventField>? context,
        MessagePackSerializerOptions options)
    {
        if (context is null)
        {
            writer.WriteArrayHeader(0);
            return;
        }

        writer.WriteArrayHeader(context.Count);
        for (int i = 0; i < context.Count; i++)
            WriteField(ref writer, context[i].Name, context[i].Value, options);
    }

    private static void WriteField(
        ref MessagePackWriter writer,
        string name,
        ProjectedEventValue value,
        MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(ProjectedEventFieldPropertyCount);
        writer.Write(nameof(ProjectedEventField.Name));
        writer.Write(name);
        writer.Write(nameof(ProjectedEventField.Value));
        MessagePackSerializer.Serialize(ref writer, value, options);
    }

    private static int EstimateCapacity(
        string eventType,
        string eventName,
        int fieldCount,
        int contextCount)
    {
        int fields = fieldCount + contextCount;
        int estimate = 160 + eventType.Length + eventName.Length + fields * 80;
        return Math.Max(256, estimate);
    }
}

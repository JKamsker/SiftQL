using System.Buffers;
using SiftQL;
using SiftQL.Projected;
using MessagePack;

namespace SiftQL.Projection;

internal static class ProjectedPayloadWriter
{
    private const int ProjectedEventPropertyCount = 4;
    private const int ProjectedEventFieldPropertyCount = 2;
    private const int ProjectedEventValuePropertyCount = 9;

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
            if (field.WritePayload is { } writePayload)
                writePayload(ref writer, subject, options);
            else
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

    internal static void WriteNullField(
        ref MessagePackWriter writer,
        string name,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.Null,
            boolean: false,
            integer: 0,
            unsignedInteger: 0,
            number: 0,
            text: null,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteBooleanField(
        ref MessagePackWriter writer,
        string name,
        bool value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.Boolean,
            value,
            integer: 0,
            unsignedInteger: 0,
            number: 0,
            text: null,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteIntegerField(
        ref MessagePackWriter writer,
        string name,
        long value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.Integer,
            boolean: false,
            value,
            unsignedInteger: 0,
            number: 0,
            text: null,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteUnsignedIntegerField(
        ref MessagePackWriter writer,
        string name,
        ulong value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.UnsignedInteger,
            boolean: false,
            integer: 0,
            value,
            number: 0,
            text: null,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteNumberField(
        ref MessagePackWriter writer,
        string name,
        double value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.Number,
            boolean: false,
            integer: 0,
            unsignedInteger: 0,
            value,
            text: null,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteStringField(
        ref MessagePackWriter writer,
        string name,
        string value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.String,
            boolean: false,
            integer: 0,
            unsignedInteger: 0,
            number: 0,
            value,
            guid: Guid.Empty,
            options);
    }

    internal static void WriteGuidField(
        ref MessagePackWriter writer,
        string name,
        Guid value,
        MessagePackSerializerOptions options)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            ProjectedEventValueKind.Guid,
            boolean: false,
            integer: 0,
            unsignedInteger: 0,
            number: 0,
            text: null,
            value,
            options);
    }

    private static void WriteFieldHeader(ref MessagePackWriter writer, string name)
    {
        writer.WriteMapHeader(ProjectedEventFieldPropertyCount);
        writer.Write(nameof(ProjectedEventField.Name));
        writer.Write(name);
        writer.Write(nameof(ProjectedEventField.Value));
    }

    private static void WriteScalarValue(
        ref MessagePackWriter writer,
        ProjectedEventValueKind kind,
        bool boolean,
        long integer,
        ulong unsignedInteger,
        double number,
        string? text,
        Guid guid,
        MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(ProjectedEventValuePropertyCount);
        writer.Write(nameof(ProjectedEventValue.Kind));
        writer.Write((int)kind);
        writer.Write(nameof(ProjectedEventValue.Boolean));
        writer.Write(boolean);
        writer.Write(nameof(ProjectedEventValue.Integer));
        writer.Write(integer);
        writer.Write(nameof(ProjectedEventValue.UnsignedInteger));
        writer.Write(unsignedInteger);
        writer.Write(nameof(ProjectedEventValue.Number));
        writer.Write(number);
        writer.Write(nameof(ProjectedEventValue.String));
        if (text is null)
            writer.WriteNil();
        else
            writer.Write(text);
        writer.Write(nameof(ProjectedEventValue.Guid));
        MessagePackSerializer.Serialize(ref writer, guid, options);
        writer.Write(nameof(ProjectedEventValue.Values));
        writer.WriteArrayHeader(0);
        writer.Write(nameof(ProjectedEventValue.Fields));
        writer.WriteArrayHeader(0);
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

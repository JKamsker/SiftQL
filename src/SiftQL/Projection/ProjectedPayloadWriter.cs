using System.Buffers;
using SiftQL;
using SiftQL.Projected;
using MessagePack;

namespace SiftQL.Projection;

internal static class ProjectedPayloadWriter
{
    private const int ProjectedEventPropertyCount = 4;
    private const int ProjectedEventFieldPropertyCount = 2;
    private const int ProjectedEventValuePropertyCount = 10;
    private const int MaxRetainedBufferBytes = 64 * 1024;

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? t_buffer;

    public static ReadOnlyMemory<byte> Write<TContext>(
        string eventType,
        string eventName,
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        object subject,
        IReadOnlyList<ProjectedEventField>? context,
        MessagePackSerializerOptions options)
    {
        ArrayBufferWriter<byte> buffer = RentBuffer(
            EstimateCapacity(eventType, eventName, fields.Count, context?.Count ?? 0),
            out int rentedCapacity);
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
        return CopyWrittenPayload(buffer, rentedCapacity);
    }

    public static ReadOnlyMemory<byte> Write(
        string eventType,
        string eventName,
        IReadOnlyList<ProjectedEventField> fields,
        IReadOnlyList<ProjectedEventField>? context,
        MessagePackSerializerOptions options)
    {
        ArrayBufferWriter<byte> buffer = RentBuffer(
            EstimateCapacity(eventType, eventName, fields.Count, context?.Count ?? 0),
            out int rentedCapacity);
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(ProjectedEventPropertyCount);
        writer.Write(nameof(ProjectedEvent.EventType));
        writer.Write(eventType);
        writer.Write(nameof(ProjectedEvent.EventName));
        writer.Write(eventName);
        writer.Write(nameof(ProjectedEvent.Fields));
        WriteFields(ref writer, fields, options);
        writer.Write(nameof(ProjectedEvent.Context));
        WriteContext(ref writer, context, options);
        writer.Flush();
        return CopyWrittenPayload(buffer, rentedCapacity);
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

        int count = 0;
        for (int i = 0; i < context.Count; i++)
        {
            if (context[i] is not null)
                count++;
        }

        writer.WriteArrayHeader(count);
        for (int i = 0; i < context.Count; i++)
        {
            ProjectedEventField? field = context[i];
            if (field is not null)
                WriteField(ref writer, field.Name, field.Value, options);
        }
    }

    private static void WriteFields(
        ref MessagePackWriter writer,
        IReadOnlyList<ProjectedEventField> fields,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(fields.Count);
        for (int i = 0; i < fields.Count; i++)
            WriteField(ref writer, fields[i].Name, fields[i].Value, options);
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
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Null, options);

    internal static void WriteBooleanField(
        ref MessagePackWriter writer,
        string name,
        bool value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Boolean, options, boolean: value);

    internal static void WriteIntegerField(
        ref MessagePackWriter writer,
        string name,
        long value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Integer, options, integer: value);

    internal static void WriteUnsignedIntegerField(
        ref MessagePackWriter writer,
        string name,
        ulong value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.UnsignedInteger, options, unsignedInteger: value);

    internal static void WriteNumberField(
        ref MessagePackWriter writer,
        string name,
        double value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Number, options, number: value);

    internal static void WriteDecimalField(
        ref MessagePackWriter writer,
        string name,
        decimal value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Decimal, options, exactDecimal: value);

    internal static void WriteStringField(
        ref MessagePackWriter writer,
        string name,
        string value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.String, options, text: value);

    internal static void WriteGuidField(
        ref MessagePackWriter writer,
        string name,
        Guid value,
        MessagePackSerializerOptions options) =>
        WriteScalarField(ref writer, name, ProjectedEventValueKind.Guid, options, guid: value);

    private static void WriteScalarField(
        ref MessagePackWriter writer,
        string name,
        ProjectedEventValueKind kind,
        MessagePackSerializerOptions options,
        bool boolean = false,
        long integer = 0,
        ulong unsignedInteger = 0,
        double number = 0,
        decimal exactDecimal = 0,
        string? text = null,
        Guid guid = default)
    {
        WriteFieldHeader(ref writer, name);
        WriteScalarValue(
            ref writer,
            kind,
            boolean,
            integer,
            unsignedInteger,
            number,
            exactDecimal,
            text,
            guid,
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
        decimal exactDecimal,
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
        writer.Write(nameof(ProjectedEventValue.Decimal));
        MessagePackSerializer.Serialize(ref writer, exactDecimal, options);
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

    private static ArrayBufferWriter<byte> RentBuffer(int capacity, out int rentedCapacity)
    {
        ArrayBufferWriter<byte>? buffer = t_buffer;
        if (buffer is null)
        {
            buffer = new ArrayBufferWriter<byte>(capacity);
            t_buffer = buffer;
            rentedCapacity = buffer.Capacity;
            return buffer;
        }

        buffer.Clear();
        if (buffer.Capacity >= capacity)
        {
            rentedCapacity = buffer.Capacity;
            return buffer;
        }

        buffer = new ArrayBufferWriter<byte>(capacity);
        t_buffer = buffer;
        rentedCapacity = buffer.Capacity;
        return buffer;
    }

    private static ReadOnlyMemory<byte> CopyWrittenPayload(ArrayBufferWriter<byte> buffer, int rentedCapacity)
    {
        byte[] payload = buffer.WrittenSpan.ToArray();
        if (buffer.Capacity > MaxRetainedBufferBytes)
        {
            t_buffer = null;
        }
        else if (buffer.Capacity > rentedCapacity)
        {
            t_buffer = new ArrayBufferWriter<byte>(buffer.Capacity);
        }

        return payload;
    }
}

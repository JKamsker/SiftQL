using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShaRPC.Core.Serialization;

namespace SiftQL.Examples.ShaRpc.SharedContracts.Serialization;

public static class JsonRpcSerializerFactory
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public static ISerializer Create() => new JsonRpcSerializer();

    private sealed class JsonRpcSerializer : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            ArgumentNullException.ThrowIfNull(writer);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, s_options);
            writer.Write(bytes);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload) =>
            JsonSerializer.Deserialize<T>(payload.Span, s_options)
            ?? throw new JsonException($"Payload could not be deserialized as '{typeof(T).FullName}'.");

        public object Deserialize(ReadOnlyMemory<byte> payload, Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return JsonSerializer.Deserialize(payload.Span, type, s_options)
                ?? throw new JsonException($"Payload could not be deserialized as '{type.FullName}'.");
        }
    }
}

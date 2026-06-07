using MessagePack;

namespace SiftQL.Projection;

internal delegate void ProjectedFieldPayloadWriter(
    ref MessagePackWriter writer,
    object subject,
    MessagePackSerializerOptions options);

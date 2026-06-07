using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SiftQL.Hot;

internal static class HotTieredProviderAssemblyMetadata
{
    public static IReadOnlyDictionary<string, string> Read(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            throw new BadImageFormatException("Hot provider DLL does not contain metadata.");

        MetadataReader reader = pe.GetMetadataReader();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (!IsAssemblyMetadataAttribute(reader, attribute.Constructor))
                continue;

            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1)
                continue;

            string? key = blob.ReadSerializedString();
            string? value = blob.ReadSerializedString();
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
                values[key] = value;
        }

        return values;
    }

    private static bool IsAssemblyMetadataAttribute(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };
        return TryGetTypeName(reader, type, out string? ns, out string? name) &&
            ns == "System.Reflection" &&
            name == "AssemblyMetadataAttribute";
    }

    private static bool TryGetTypeName(
        MetadataReader reader,
        EntityHandle type,
        out string? ns,
        out string? name)
    {
        if (type.Kind == HandleKind.TypeReference)
        {
            TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)type);
            ns = reader.GetString(reference.Namespace);
            name = reader.GetString(reference.Name);
            return true;
        }

        if (type.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition definition = reader.GetTypeDefinition((TypeDefinitionHandle)type);
            ns = reader.GetString(definition.Namespace);
            name = reader.GetString(definition.Name);
            return true;
        }

        ns = null;
        name = null;
        return false;
    }
}

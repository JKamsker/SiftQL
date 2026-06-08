using System.Collections.Concurrent;

namespace SiftQL;

public static class SiftQueryContextRegistry
{
    private static readonly ConcurrentDictionary<Type, SiftQueryContextDescriptor> s_descriptors = new();

    public static void Register(SiftQueryContextDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(descriptor.ContextType);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ContextId);

        s_descriptors[descriptor.ContextType] = descriptor;
    }

    public static bool TryGet(Type contextType, out SiftQueryContextDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        return s_descriptors.TryGetValue(contextType, out descriptor!);
    }
}

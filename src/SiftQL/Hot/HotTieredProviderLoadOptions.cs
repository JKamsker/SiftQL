using System.Reflection;
using System.Runtime.Loader;
using SiftQL.Schema;

namespace SiftQL.Hot;

public sealed record HotTieredProviderLoadOptions
{
    public string AssemblyPath { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public bool RequireExactRuntimeVersion { get; init; } = true;
}

public enum HotTieredProviderLoadStatus
{
    Loaded,
    MissingArtifact,
    InvalidManifest,
    VersionMismatch,
    ManifestHashMismatch,
    InvalidAssembly,
    LoadFailed,
}

public sealed class HotTieredProviderLoadResult : IDisposable
{
    private readonly AssemblyLoadContext? _loadContext;
    private readonly IDisposable? _registration;
    private int _disposed;

    internal HotTieredProviderLoadResult(
        HotTieredProviderLoadStatus status,
        string message,
        Assembly? assembly = null,
        AssemblyLoadContext? loadContext = null,
        IDisposable? registration = null)
    {
        Status = status;
        Message = message;
        Assembly = assembly;
        _loadContext = loadContext;
        _registration = registration;
    }

    public HotTieredProviderLoadStatus Status { get; }
    public string Message { get; }
    public Assembly? Assembly { get; }
    public bool Loaded => Status == HotTieredProviderLoadStatus.Loaded;

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (Assembly is not null)
            FilterSchema.UnregisterGeneratedProvider(Assembly);

        _registration?.Dispose();
        if (Assembly is not null)
            PrecompiledTieredProviderRegistry.RemoveAssembly(Assembly);
        _loadContext?.Unload();
    }
}

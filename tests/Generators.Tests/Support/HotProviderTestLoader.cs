using System.Reflection;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

internal sealed class LoadedHotProvider : IDisposable
{
    private readonly string _directory;
    private readonly HotTieredProviderLoadResult _result;

    public LoadedHotProvider(string directory, HotTieredProviderLoadResult result)
    {
        _directory = directory;
        _result = result;
    }

    public Assembly Assembly => _result.Assembly!;

    public void Dispose()
    {
        _result.Dispose();
        TryDeleteDirectory(_directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Test cleanup is best effort because collectible assemblies may release asynchronously.
        }
    }
}

internal static class HotProviderTestLoader
{
    public static LoadedHotProvider Load(
        Compilation compilation,
        string assemblyName,
        string manifestJson,
        string label)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotProviderTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "siftql-filter-hot-manifest.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
        AssertEx.True(result.Loaded, label + " loaded: " + result.Message);
        return new LoadedHotProvider(directory, result);
    }
}

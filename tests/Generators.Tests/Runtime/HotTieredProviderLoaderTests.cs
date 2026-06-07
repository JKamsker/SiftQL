using System.Text.Json;
using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotTieredProviderLoaderTests
{
    [Fact]
    public void MissingArtifact_WhenAssemblyDoesNotExist()
    {
        string directory = CreateTempDirectory("MissingArtifact");
        string manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "nonexistent.dll"),
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.MissingArtifact, result.Status);
        Assert.False(result.Loaded);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void MissingArtifact_WhenManifestDoesNotExist()
    {
        string directory = CreateTempDirectory("MissingManifest");
        string assemblyPath = Path.Combine(directory, "fake.dll");
        File.WriteAllBytes(assemblyPath, [0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = Path.Combine(directory, "nonexistent.json"),
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.MissingArtifact, result.Status);
    }

    [Fact]
    public void InvalidManifest_WhenManifestDeserializesToNull()
    {
        string directory = CreateTempDirectory("NullManifest");
        string manifestPath = Path.Combine(directory, "manifest.json");
        string assemblyPath = Path.Combine(directory, "fake.dll");
        File.WriteAllText(manifestPath, "null");
        File.WriteAllBytes(assemblyPath, [0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public void VersionMismatch_WhenSchemaDoesNotMatch()
    {
        string directory = CreateTempDirectory("SchemaMismatch");
        var manifest = new HotCompilationManifest { Schema = "wrong.schema.v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("manifest schema", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenFilterEngineDoesNotMatch()
    {
        string directory = CreateTempDirectory("EngineMismatch");
        var manifest = new HotCompilationManifest { FilterEngineVersion = "wrong-engine-v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("filter engine", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenGeneratorDoesNotMatch()
    {
        string directory = CreateTempDirectory("GeneratorMismatch");
        var manifest = new HotCompilationManifest { GeneratorVersion = "wrong-gen-v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("generator", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenRuntimeVersionRequired()
    {
        string directory = CreateTempDirectory("RuntimeMismatch");
        var manifest = new HotCompilationManifest { RuntimeVersion = "99.99.99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "hot.dll"),
            ManifestPath = Path.Combine(directory, "hot.json"),
            RequireExactRuntimeVersion = true,
        });

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("runtime", result.Message);
    }

    [Fact]
    public void InvalidAssembly_WhenBadImageFormat()
    {
        string directory = CreateTempDirectory("BadImage");
        var manifest = new HotCompilationManifest { RuntimeVersion = "10.0.0" };
        string manifestJson = JsonSerializer.Serialize(manifest);
        string manifestPath = Path.Combine(directory, "hot.json");
        string assemblyPath = Path.Combine(directory, "hot.dll");
        File.WriteAllText(manifestPath, manifestJson);
        File.WriteAllBytes(assemblyPath, [0x4D, 0x5A, 0x00, 0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.True(
            result.Status == HotTieredProviderLoadStatus.InvalidAssembly ||
            result.Status == HotTieredProviderLoadStatus.LoadFailed,
            $"Expected InvalidAssembly or LoadFailed but got {result.Status}: {result.Message}");
    }

    [Fact]
    public void FailedLoadResult_IsNotLoaded()
    {
        string directory = CreateTempDirectory("FailedResult");
        string manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "nonexistent.dll"),
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.False(result.Loaded);
        Assert.Null(result.Assembly);
        result.Dispose();
        result.Dispose();
    }

    private static string CreateTempDirectory(string suffix)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "SiftQLWave6Tests", suffix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteManifestAndFakeAssembly(string directory, HotCompilationManifest manifest)
    {
        string manifestJson = JsonSerializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(directory, "hot.json"), manifestJson);
        File.WriteAllBytes(Path.Combine(directory, "hot.dll"), [0x00]);
    }

    private static HotTieredProviderLoadResult LoadFromDirectory(string directory) =>
        HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "hot.dll"),
            ManifestPath = Path.Combine(directory, "hot.json"),
            RequireExactRuntimeVersion = false,
        });
}

using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotManifestRequiredFieldsRegressionTests
{
    [Fact]
    public void LoaderRejectsManifestMissingCompatibilityFieldsBeforeAssemblyRead()
    {
        string directory = CreateTempDirectory("MissingCompatibility");
        string manifestPath = Path.Combine(directory, "hot.json");
        string assemblyPath = Path.Combine(directory, "hot.dll");
        File.WriteAllText(manifestPath, """{"Entries":[]}""");
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
    public void ManifestWriterDropsExistingEntriesWithMissingCompatibilityFields()
    {
        string path = TempManifestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ExistingManifestMissingCompatibilityFields());
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        Assert.DoesNotContain(manifest.Entries, static entry => entry.SubjectType == "stale");
        Assert.Single(manifest.Entries);
    }

    private static string ExistingManifestMissingCompatibilityFields()
    {
        var stale = new HotCompilationManifestEntry
        {
            Key = "filter|stale|abc",
            Kind = "filter",
            SubjectType = "stale",
            Fingerprint = "abc",
            Definition = JsonSerializer.SerializeToElement(Filter()),
            Observed = new HotCompilationObserved
            {
                LastSeenUtc = DateTimeOffset.UtcNow,
            },
        };

        return $$"""
            {
              "Entries": [
                {{JsonSerializer.Serialize(stale)}}
              ]
            }
            """;
    }

    private static FilterExpression Filter() =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));

    private static HotCompilationManifest ReadManifest(string path) =>
        JsonSerializer.Deserialize<HotCompilationManifest>(File.ReadAllText(path)) ??
        throw new InvalidOperationException("Manifest did not deserialize.");

    private static string TempManifestPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotManifestRequiredFields",
            Guid.NewGuid().ToString("N"),
            "hot.json");

    private static string CreateTempDirectory(string suffix)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotManifestRequiredFields",
            suffix,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;

namespace SiftQL.Generators.Tests;

public sealed class HotCompilationManifestWriterVersionRegressionTests
{
    [Fact]
    public void ManifestWriterDropsExistingEntriesFromMismatchedEngineVersion()
    {
        string path = TempManifestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new HotCompilationManifest
        {
            FilterEngineVersion = "old-engine",
            Entries =
            [
                new HotCompilationManifestEntry
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
                },
            ],
        }));
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        Assert.DoesNotContain(manifest.Entries, static entry => entry.SubjectType == "stale");
        Assert.Single(manifest.Entries);
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
            "SiftQLHotManifestWriter",
            Guid.NewGuid().ToString("N"),
            "hot.json");
}

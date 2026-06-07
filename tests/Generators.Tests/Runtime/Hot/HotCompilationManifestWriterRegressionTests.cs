using System.Text.Json;
using SiftQL.Expressions;
using SiftQL.Hot;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotCompilationManifestWriterRegressionTests
{
    [Fact]
    public void ManifestWriterDisposeFlushesQueuedWrite()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotManifestWriter",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "hot.json");
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));

        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.FromSeconds(1) });
        writer.RecordHotFilter(typeof(ItemUsedEvent), filter, evaluations: 1, matches: 1);
        writer.Dispose();

        Assert.True(File.Exists(path));
        HotCompilationManifest manifest = JsonSerializer.Deserialize<HotCompilationManifest>(
            File.ReadAllText(path))!;
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("filter", entry.Kind);
    }
}

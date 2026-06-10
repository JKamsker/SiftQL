using System.Text.Json;
using SiftQL.Expressions;
using SiftQL.Hot;

namespace SiftQL.Generators.Tests;

public sealed class HotUnsupportedFilterManifestRegressionTests
{
    public static TheoryData<FilterExpression> UnsupportedFilters() =>
        new()
        {
            FilterExpression.Between(
                nameof(UnsupportedHotEvent.Quantity),
                FilterValue.From(1L),
                FilterValue.From(3L)),
            FilterExpression.Count(
                nameof(UnsupportedHotEvent.Tags),
                FilterOperator.GreaterThan,
                FilterValue.From(1L)),
            FilterExpression.ElemMatch(
                nameof(UnsupportedHotEvent.Items),
                FilterExpression.Compare(
                    nameof(UnsupportedHotItem.Name),
                    FilterOperator.Equal,
                    FilterValue.From("rare"))),
        };

    [Theory]
    [MemberData(nameof(UnsupportedFilters))]
    public void ManifestWriterSkipsUnsupportedHotFilterNodes(FilterExpression filter)
    {
        string path = TempManifestPath();
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(UnsupportedHotEvent), filter, evaluations: 1, matches: 0);
        writer.Flush();

        if (!File.Exists(path))
            return;

        HotCompilationManifest manifest = JsonSerializer.Deserialize<HotCompilationManifest>(
            File.ReadAllText(path))!;
        Assert.Empty(manifest.Entries);
    }

    [Theory]
    [MemberData(nameof(UnsupportedFilters))]
    public async Task BatchSinkSkipsUnsupportedHotFilterNodes(FilterExpression filter)
    {
        var queue = new RuntimeHotProviderBatchTestSupport.RecordingBatchQueue();
        using var sink = new RuntimeHotProviderBatchSink(
            queue,
            new RuntimeHotProviderBatchOptions
            {
                MinimumEntries = 1,
                MinimumInterval = TimeSpan.Zero,
            });

        sink.RecordHotFilter(typeof(UnsupportedHotEvent), filter, evaluations: 1, matches: 0);
        await Task.Delay(50);

        Assert.False(queue.HasBatch);
    }

    private static string TempManifestPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotUnsupportedFilters",
            Guid.NewGuid().ToString("N"),
            "hot.json");

    private sealed record UnsupportedHotEvent(
        long Quantity,
        string[] Tags,
        UnsupportedHotItem[] Items) : IFilterSubject;

    private sealed record UnsupportedHotItem(string Name);
}

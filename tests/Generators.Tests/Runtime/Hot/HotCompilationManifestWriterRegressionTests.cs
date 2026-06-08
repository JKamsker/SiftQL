using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotCompilationManifestWriterRegressionTests
{
    [Fact]
    public void ManifestWriterPersistsFilterAndProjectionEntries()
    {
        string path = TempManifestPath();
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 12, matches: 3);
        writer.RecordHotProjection(typeof(ItemUsedEvent), Projection(), materializations: 7, payloadWrites: 2);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);

        Assert.Equal(2, manifest.Entries.Length);
        Assert.Contains(manifest.Entries, static entry =>
            entry.Kind == "filter" &&
            entry.Observed.Evaluations == 12 &&
            entry.Observed.Matches == 3);
        Assert.Contains(manifest.Entries, static entry =>
            entry.Kind == "projection" &&
            entry.Observed.Materializations == 7 &&
            entry.Observed.PayloadWrites == 2);
    }

    [Fact]
    public void ManifestWriterDecaysStaleEntries()
    {
        string path = TempManifestPath();
        var stale = new HotCompilationManifest
        {
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
                        LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(10),
                    },
                },
            ],
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(stale));
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions
            {
                CoalesceDelay = TimeSpan.Zero,
                Retention = TimeSpan.FromDays(1),
            });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        Assert.DoesNotContain(manifest.Entries, static entry => entry.SubjectType == "stale");
        Assert.Single(manifest.Entries);
    }

    [Fact]
    public void ManifestWriterRecoversFromMalformedExistingManifest()
    {
        string path = TempManifestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken json");

        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("filter", entry.Kind);
    }

    [Fact]
    public void ManifestWriterDropsExistingEntriesMissingDefinition()
    {
        string path = TempManifestPath();
        string seen = DateTimeOffset.UtcNow.ToString("O");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            {
              "Schema": "siftql.hot.v1",
              "RuntimeVersion": "{{Environment.Version}}",
              "FilterEngineVersion": "tiered-v1",
              "GeneratorVersion": "hot-sourcegen-v1",
              "Entries": [
                {
                  "Key": "filter|stale|abc",
                  "Kind": "filter",
                  "SubjectType": "stale",
                  "Fingerprint": "abc",
                  "Observed": { "LastSeenUtc": "{{seen}}" }
                }
              ]
            }
            """);
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions
            {
                CoalesceDelay = TimeSpan.Zero,
                Retention = TimeSpan.FromDays(1),
            });

        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        Assert.DoesNotContain(manifest.Entries, static entry => entry.SubjectType == "stale");
        Assert.Single(manifest.Entries);
    }

    [Fact]
    public void ManifestWriterSkipsRuntimeAcceptedNaNFilter()
    {
        string path = TempManifestPath();
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(double.NaN));

        writer.RecordHotFilter(typeof(ItemUsedEvent), filter, evaluations: 1, matches: 0);
        writer.Flush();

        if (!File.Exists(path))
            return;

        Assert.Empty(ReadManifest(path).Entries);
    }

    [Fact]
    public void ManifestWriterSkipsRuntimeAcceptedNaNProjectionArgument()
    {
        string path = TempManifestPath();
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });
        EventProjectionExpression projection = EventProjectionExpression
            .Select(nameof(ItemUsedEvent.ItemId))
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "test.window",
                    "window",
                    new EventProjectionArgument("offset", FilterValue.From(double.NaN))),
            ]);

        Exception? exception = Record.Exception(() =>
            writer.RecordHotProjection(typeof(ItemUsedEvent), projection, materializations: 1, payloadWrites: 0));
        writer.Flush();

        Assert.Null(exception);
        if (!File.Exists(path))
            return;

        Assert.Empty(ReadManifest(path).Entries);
    }

    [Fact]
    public void ManifestWriterDisposeFlushesQueuedWrite()
    {
        string path = TempManifestPath();

        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.FromSeconds(1) });
        writer.RecordHotFilter(typeof(ItemUsedEvent), Filter(), evaluations: 1, matches: 1);
        writer.Dispose();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("filter", entry.Kind);
    }

    [Fact]
    public void HotTieredFilterReportsManifestSink()
    {
        var sink = new RecordingSink();
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            Filter(),
            FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumEvaluations = 1,
                HotManifestSink = sink,
            });

        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2)));

        Assert.Equal(1, sink.FilterCalls);
        Assert.Equal(typeof(ItemUsedEvent), sink.FilterSubjectType);
        Assert.Equal(1, sink.FilterEvaluations);
        Assert.Equal(1, sink.FilterMatches);
    }

    [Fact]
    public async Task HotTieredProjectionReportsManifestSink()
    {
        var sink = new RecordingSink();
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            Projection(),
            RejectInclude,
            ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.Zero,
                TieredPromotionMinimumOperations = 1,
                HotManifestSink = sink,
            });

        await projection.ProjectAsync(
            new ItemUsedEvent(Guid.NewGuid(), 10, 100, 2),
            new object(),
            CancellationToken.None);

        Assert.Equal(1, sink.ProjectionCalls);
        Assert.Equal(typeof(ItemUsedEvent), sink.ProjectionSubjectType);
        Assert.Equal(1, sink.ProjectionMaterializations);
        Assert.Equal(0, sink.ProjectionPayloadWrites);
    }

    private static FilterExpression Filter() =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));

    private static EventProjectionExpression Projection() =>
        EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static HotCompilationManifest ReadManifest(string path)
    {
        for (int i = 0; i < 50; i++)
        {
            try
            {
                return JsonSerializer.Deserialize<HotCompilationManifest>(
                    File.ReadAllText(path)) ?? throw new InvalidOperationException("Manifest did not deserialize.");
            }
            catch (IOException) when (i < 49)
            {
                Thread.Sleep(10);
            }
        }

        throw new IOException($"Manifest '{path}' stayed locked.");
    }

    private static string TempManifestPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotManifestWriter",
            Guid.NewGuid().ToString("N"),
            "hot.json");

    private sealed class RecordingSink : ITieredHotManifestSink
    {
        public int FilterCalls { get; private set; }
        public Type? FilterSubjectType { get; private set; }
        public long FilterEvaluations { get; private set; }
        public long FilterMatches { get; private set; }
        public int ProjectionCalls { get; private set; }
        public Type? ProjectionSubjectType { get; private set; }
        public long ProjectionMaterializations { get; private set; }
        public long ProjectionPayloadWrites { get; private set; }

        public void RecordHotFilter(
            Type subjectType,
            FilterExpression expression,
            long evaluations,
            long matches)
        {
            _ = expression;
            FilterCalls++;
            FilterSubjectType = subjectType;
            FilterEvaluations = evaluations;
            FilterMatches = matches;
        }

        public void RecordHotProjection(
            Type subjectType,
            EventProjectionExpression projection,
            long materializations,
            long payloadWrites)
        {
            _ = projection;
            ProjectionCalls++;
            ProjectionSubjectType = subjectType;
            ProjectionMaterializations = materializations;
            ProjectionPayloadWrites = payloadWrites;
        }
    }
}

using System.Reflection;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;

namespace SiftQL.Generators.Tests;

public sealed class HotCompilationManifestExistingEntryRegressionTests
{
    [Fact]
    public void ManifestWriterDropsExistingUnsupportedAndMismatchedFilterEntries()
    {
        string path = TempManifestPath();
        FilterExpression valid = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        FilterExpression unsupported = FilterExpression.Count(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.GreaterThan,
            FilterValue.From(0L));
        FilterExpression mismatched = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(200L));
        SeedManifest(path, unsupported, mismatched);
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), valid, evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(Fingerprint(valid), entry.Fingerprint);
    }

    [Fact]
    public void ManifestWriterDropsExistingFilterEntriesWithNullArrays()
    {
        string path = TempManifestPath();
        FilterExpression valid = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        SeedRawManifest(path, NullArrayEntry(valid));
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), valid, evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(Fingerprint(valid), entry.Fingerprint);
    }

    [Fact]
    public void ManifestWriterDropsExistingFilterEntriesWithNullChildren()
    {
        string path = TempManifestPath();
        FilterExpression valid = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        SeedRawManifest(path, NullChildEntry(valid));
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), valid, evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(Fingerprint(valid), entry.Fingerprint);
    }

    [Fact]
    public void ManifestWriterDropsExistingSemanticallyInvalidFilterEntries()
    {
        string path = TempManifestPath();
        FilterExpression valid = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        FilterExpression invalid = new(FilterExpressionKind.Not) { Children = [] };
        SeedRawManifest(path, Entry("invalid-shape", Fingerprint(invalid), invalid));
        var writer = new HotCompilationManifestWriter(
            path,
            new HotCompilationManifestWriterOptions { CoalesceDelay = TimeSpan.Zero });

        writer.RecordHotFilter(typeof(ItemUsedEvent), valid, evaluations: 1, matches: 1);
        writer.Flush();

        HotCompilationManifest manifest = ReadManifest(path);
        HotCompilationManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(Fingerprint(valid), entry.Fingerprint);
    }

    [Fact]
    public void ExistingEntryInspectionTreatsUnreadableDefinitionsAsInvalid()
    {
        HotCompilationManifestEntry entry;
        using (JsonDocument document = JsonDocument.Parse("""{"Kind":4}"""))
            entry = Entry("disposed-definition", "fingerprint", document.RootElement);

        Assert.False(IsValidExistingEntry(entry));
    }

    private static void SeedManifest(
        string path,
        FilterExpression unsupported,
        FilterExpression mismatched)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new HotCompilationManifest
            {
                Entries =
                [
                    Entry("unsupported", Fingerprint(unsupported), unsupported),
                    Entry("mismatched", "not-" + Fingerprint(mismatched), mismatched),
                ],
            }));
    }

    private static void SeedRawManifest(
        string path,
        HotCompilationManifestEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new HotCompilationManifest { Entries = [entry] }));
    }

    private static HotCompilationManifestEntry NullArrayEntry(FilterExpression definition)
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "Kind": 4,
              "Field": "{{nameof(ItemUsedEvent.ItemId)}}",
              "Operator": 0,
              "Value": { "Kind": 2, "Integer": 100 },
              "Values": null,
              "Children": null
            }
            """);
        return Entry("null-arrays", Fingerprint(definition), document.RootElement.Clone());
    }

    private static HotCompilationManifestEntry NullChildEntry(FilterExpression definition)
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "Kind": 3,
              "Children": [null]
            }
            """);
        return Entry("null-child", Fingerprint(definition), document.RootElement.Clone());
    }

    private static HotCompilationManifestEntry Entry(
        string key,
        string fingerprint,
        FilterExpression definition) =>
        new()
        {
            Key = key,
            Kind = "filter",
            SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
            Fingerprint = fingerprint,
            Definition = JsonSerializer.SerializeToElement(definition),
            Observed = new HotCompilationObserved
            {
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow,
            },
        };

    private static HotCompilationManifestEntry Entry(
        string key,
        string fingerprint,
        JsonElement definition) =>
        new()
        {
            Key = key,
            Kind = "filter",
            SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
            Fingerprint = fingerprint,
            Definition = definition,
            Observed = new HotCompilationObserved
            {
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow,
            },
        };

    private static string Fingerprint(FilterExpression expression) =>
        FilterExpressionFingerprint.CreateKey(expression).ToString();

    private static bool IsValidExistingEntry(HotCompilationManifestEntry entry)
    {
        MethodInfo method = typeof(HotCompilationManifestWriter).GetMethod(
            "IsValidExistingEntry",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [entry])!;
    }

    private static HotCompilationManifest ReadManifest(string path) =>
        JsonSerializer.Deserialize<HotCompilationManifest>(File.ReadAllText(path)) ??
        throw new InvalidOperationException("Manifest did not deserialize.");

    private static string TempManifestPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotExistingManifest",
            Guid.NewGuid().ToString("N"),
            "hot.json");
}

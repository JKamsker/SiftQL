using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderValidationTests
{
    [Fact]
    public void RejectsEventMetadataFieldOnNormalSubject()
    {
        var filter = FilterExpression.Compare(
            "eventType",
            FilterOperator.Equal,
            FilterValue.From("Plugin.Events.PluginOwnedEvent"));
        GeneratorRun run = RunGenerator(
            Manifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                Fingerprint(filter),
                filter),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "normal subject event metadata diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "normal subject event metadata emitted no source");
    }

    [Fact]
    public void RejectsProjectedFieldsWithEmptyNames()
    {
        var projection = EventProjectionExpression.Select(ProjectedEventPaths.FieldPrefix);
        GeneratorRun run = RunGenerator(
            Manifest(
                "projection",
                typeof(ProjectedEvent).FullName + ", " + typeof(ProjectedEvent).Assembly.GetName().Name,
                "projected-empty-field",
                projection),
            CSharpSyntaxTree.ParseText("namespace Plugin.Events;"));

        AssertDiagnostic(run, "FSFHOT009", "empty projected field diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid projected field emitted no hot provider source");
    }

    [Fact]
    public void RejectsFiltersOverTotalNodeLimit()
    {
        var children = Enumerable.Range(0, 129)
            .Select(static _ => FilterExpression.Compare(
                "CharacterId",
                FilterOperator.Equal,
                FilterValue.From(1L)))
            .ToArray();
        FilterExpression filter = FilterExpression.And(children);
        GeneratorRun run = RunGenerator(
            Manifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                "too-many-nodes",
                filter),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "total node limit diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "oversized filter emitted no hot provider source");
    }

    [Fact]
    public void RejectsFiltersWithMissingNodeKind()
    {
        GeneratorRun run = RunGenerator(
            RawDefinitionManifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                "missing-node-kind",
                """
                {
                  "Field": "CharacterId",
                  "Operator": 0,
                  "Value": { "Kind": 2, "Integer": 1 }
                }
                """),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "missing hot filter node kind diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "missing node kind emitted no hot provider source");
    }

    [Fact]
    public void RejectsFiltersWithMissingValueKind()
    {
        GeneratorRun run = RunGenerator(
            RawDefinitionManifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                "missing-value-kind",
                """
                {
                  "Kind": 4,
                  "Field": "CharacterId",
                  "Operator": 0,
                  "Value": { "Integer": 1 }
                }
                """),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "missing hot filter value kind diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "missing value kind emitted no hot provider source");
    }

    [Fact]
    public void RejectsFiltersWithInvalidOperator()
    {
        GeneratorRun run = RunGenerator(
            RawDefinitionManifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                "invalid-operator",
                """
                {
                  "Kind": 4,
                  "Field": "CharacterId",
                  "Operator": 999,
                  "Value": { "Kind": 2, "Integer": 1 }
                }
                """),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "invalid hot filter operator diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid operator emitted no hot provider source");
    }

    [Fact]
    public void RejectsFiltersWithInvalidGuidValue()
    {
        GeneratorRun run = RunGenerator(
            RawDefinitionManifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Validation",
                "invalid-guid",
                """
                {
                  "Kind": 4,
                  "Field": "EventId",
                  "Operator": 0,
                  "Value": { "Kind": 5, "Guid": "not-a-guid" }
                }
                """),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "invalid hot filter GUID diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid GUID emitted no hot provider source");
    }

    private static void AssertDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(1, count, label);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static GeneratorRun RunGenerator(AdditionalText manifest, params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = CreateCompilation(trees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(manifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        _ = outputCompilation;
        return new GeneratorRun(driver.GetRunResult(), diagnostics);
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            "Plugin.Hot.Validation",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static AdditionalText Manifest(
        string kind,
        string subjectType,
        string fingerprint,
        object definition)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = kind + "|" + subjectType + "|" + fingerprint,
                    Kind = kind,
                    SubjectType = subjectType,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(definition),
                },
            ],
        };
        return new InMemoryAdditionalText(
            "validation.siftql-hot.json",
            JsonSerializer.Serialize(manifest));
    }

    private static AdditionalText RawDefinitionManifest(
        string kind,
        string subjectType,
        string fingerprint,
        string definitionJson)
    {
        using JsonDocument document = JsonDocument.Parse(definitionJson);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = kind + "|" + subjectType + "|" + fingerprint,
                    Kind = kind,
                    SubjectType = subjectType,
                    Fingerprint = fingerprint,
                    Definition = document.RootElement.Clone(),
                },
            ],
        };
        return new InMemoryAdditionalText(
            "validation.siftql-hot.json",
            JsonSerializer.Serialize(manifest));
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static SyntaxTree PluginEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PluginOwnedEvent(Guid EventId, long CharacterId) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        ImmutableArray<Diagnostic> Diagnostics);
}

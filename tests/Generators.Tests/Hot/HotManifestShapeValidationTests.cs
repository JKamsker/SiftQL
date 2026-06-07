using System.Collections.Immutable;
using System.Text;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotManifestShapeValidationTests
{
    [Fact]
    public void RejectsNonObjectRoot()
    {
        GeneratorRun run = RunGenerator("[]");

        AssertDiagnostic(run, "FSFHOT006", "non-object manifest root diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "non-object root emitted no hot provider source");
    }

    [Fact]
    public void RejectsNullRoot()
    {
        GeneratorRun run = RunGenerator("null");

        AssertDiagnostic(run, "FSFHOT006", "null manifest root diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "null root emitted no hot provider source");
    }

    [Fact]
    public void RejectsNonObjectEntry()
    {
        GeneratorRun run = RunGenerator(Manifest("[123]"));

        AssertDiagnostic(run, "FSFHOT007", "non-object manifest entry diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "non-object entry emitted no hot provider source");
    }

    [Fact]
    public void RejectsNonObjectFilterDefinition()
    {
        GeneratorRun run = RunGenerator(Manifest("""
            [
              {
                "Kind": "filter",
                "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Shape",
                "Fingerprint": "non-object-definition",
                "Definition": 123
              }
            ]
            """));

        AssertDiagnostic(run, "FSFHOT009", "non-object filter definition diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "non-object definition emitted no hot provider source");
    }

    [Fact]
    public void RejectsNonObjectFilterValue()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("filter", """
            {
              "Kind": 4,
              "Field": "CharacterId",
              "Operator": 0,
              "Value": 123
            }
            """));

        AssertRejectedNestedShape(run, "non-object filter value diagnostic");
    }

    [Fact]
    public void RejectsNonObjectFilterValuesItem()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("filter", """
            {
              "Kind": 5,
              "Field": "CharacterId",
              "Values": [123]
            }
            """));

        AssertRejectedNestedShape(run, "non-object filter values item diagnostic");
    }

    [Fact]
    public void RejectsNonObjectFilterChild()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("filter", """
            {
              "Kind": 1,
              "Children": [123]
            }
            """));

        AssertRejectedNestedShape(run, "non-object filter child diagnostic");
    }

    [Fact]
    public void RejectsNonObjectProjectionField()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("projection", """
            {
              "Fields": [123]
            }
            """));

        AssertRejectedNestedShape(run, "non-object projection field diagnostic");
    }

    [Fact]
    public void RejectsNonObjectProjectionInclude()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("projection", """
            {
              "Includes": [123]
            }
            """));

        AssertRejectedNestedShape(run, "non-object projection include diagnostic");
    }

    [Fact]
    public void RejectsNonObjectProjectionArgument()
    {
        GeneratorRun run = RunGenerator(ManifestWithDefinition("projection", """
            {
              "Includes": [
                {
                  "Intrinsic": "test",
                  "ResultName": "result",
                  "Arguments": [123]
                }
              ]
            }
            """));

        AssertRejectedNestedShape(run, "non-object projection argument diagnostic");
    }

    private static string Manifest(string entriesJson) =>
        $$"""
        {
          "Schema": "siftql.hot.v1",
          "FilterEngineVersion": "tiered-v1",
          "GeneratorVersion": "hot-sourcegen-v1",
          "Entries": {{entriesJson}}
        }
        """;

    private static string ManifestWithDefinition(string kind, string definitionJson) =>
        Manifest($$"""
            [
              {
                "Kind": "{{kind}}",
                "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Shape",
                "Fingerprint": "malformed-nested",
                "Definition": {{definitionJson}}
              }
            ]
            """);

    private static void AssertRejectedNestedShape(GeneratorRun run, string label)
    {
        AssertDiagnostic(run, "FSFHOT009", label);
        AssertEx.Equal(0, run.Diagnostics.Count(static item => item.Id == "CS8785"), label + " no generator exception");
        AssertEx.Equal(0, HotProviderSourceCount(run), label + " emitted no hot provider source");
    }

    private static void AssertDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(1, count, label);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("shape.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        _ = outputCompilation;
        return new GeneratorRun(driver.GetRunResult(), diagnostics);
    }

    private static CSharpCompilation CreateCompilation()
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            "Plugin.Hot.Shape",
            [PluginEventTree()],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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

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
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderFilterArrayValidationRegressionTests
{
    [Fact]
    public void RejectsNonArrayFilterChildren()
    {
        AssertRejected(ManifestWithRawFilter("""
            {
              "Kind": 4,
              "Field": "CharacterId",
              "Operator": 0,
              "Value": { "Kind": 2, "Integer": 7 },
              "Children": 123
            }
            """));
    }

    [Fact]
    public void RejectsNonArrayFilterValues()
    {
        AssertRejected(ManifestWithRawFilter("""
            {
              "Kind": 4,
              "Field": "CharacterId",
              "Operator": 0,
              "Value": { "Kind": 2, "Integer": 7 },
              "Values": 123
            }
            """));
    }

    private static void AssertRejected(string manifestJson)
    {
        GeneratorRun run = RunGenerator(manifestJson);
        AssertEx.Equal(0, run.Diagnostics.Count(item => item.Id == "CS8785"), "generator did not crash with CS8785");
        AssertEx.Equal(1, run.Diagnostics.Count(item => item.Id == "FSFHOT009"), "invalid filter array diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid filter array emitted no provider");
    }

    private static string ManifestWithRawFilter(string definitionJson)
    {
        FilterExpression filter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        using JsonDocument document = JsonDocument.Parse(definitionJson);
        return Manifest(FilterExpressionFingerprint.Create(filter), document.RootElement.Clone());
    }

    private static string Manifest(string fingerprint, JsonElement definition) =>
        JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PluginOwnedEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, Plugin.Hot.FilterArrayValidation",
                    Fingerprint = fingerprint,
                    Definition = definition,
                },
            ],
        });

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("filter-array.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        _ = outputCompilation;
        return new(driver.GetRunResult(), diagnostics);
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
            "Plugin.Hot.FilterArrayValidation",
            [PluginEventTree()],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree PluginEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PluginOwnedEvent(
                Guid EventId,
                long CharacterId) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

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

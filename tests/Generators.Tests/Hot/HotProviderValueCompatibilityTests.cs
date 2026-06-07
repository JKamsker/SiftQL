using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Compiler;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Values;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderValueCompatibilityTests
{
    [Fact]
    public void RejectsStringValueForNumericField()
    {
        GeneratorRun run = RunGenerator(
            Manifest(FilterExpression.Compare(
                "CharacterId",
                FilterOperator.Equal,
                FilterValue.From("wrong"))),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "numeric field rejects string value");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid value emitted no provider");
    }

    [Fact]
    public void RejectsOrderedComparisonForStringField()
    {
        GeneratorRun run = RunGenerator(
            Manifest(FilterExpression.Compare(
                "Name",
                FilterOperator.GreaterThan,
                FilterValue.From("zed"))),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "string field rejects ordered comparison");
        AssertEx.Equal(0, HotProviderSourceCount(run), "invalid operator emitted no provider");
    }

    private static AdditionalText Manifest(FilterExpression filter)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PluginOwnedEvent|compat",
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Compat",
                    Fingerprint = "compat",
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return new InMemoryAdditionalText(
            "compat.siftql-hot.json",
            JsonSerializer.Serialize(manifest));
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
            out _,
            out ImmutableArray<Diagnostic> diagnostics);
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
            "Plugin.Hot.Compat",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree PluginEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PluginOwnedEvent(Guid EventId, long CharacterId, string Name) : IFilterSubject;
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

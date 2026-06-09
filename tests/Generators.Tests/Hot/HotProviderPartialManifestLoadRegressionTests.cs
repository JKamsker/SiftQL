using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderPartialManifestLoadRegressionTests
{
    [Fact]
    public void InvalidEntryPreventsHotProviderEmissionForWholeManifest()
    {
        const string assemblyName = "Plugin.Hot.PartialManifest";
        var validFilter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        var missingFilter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(8L));
        string manifestJson = JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                Entry(
                    "Plugin.Events.ValidEvent, " + assemblyName,
                    validFilter),
                Entry(
                    "Plugin.Events.MissingEvent, " + assemblyName,
                    missingFilter),
            ],
        });

        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("partial.siftql-hot.json", manifestJson),
            CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record ValidEvent(long CharacterId) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    private static HotCompilationManifestEntry Entry(
        string subjectType,
        FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        return new HotCompilationManifestEntry
        {
            Key = "filter|" + subjectType + "|" + fingerprint,
            Kind = "filter",
            SubjectType = subjectType,
            Fingerprint = fingerprint,
            Definition = JsonSerializer.SerializeToElement(filter),
        };
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(
                new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        params SyntaxTree[] extraTrees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: extraTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

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
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

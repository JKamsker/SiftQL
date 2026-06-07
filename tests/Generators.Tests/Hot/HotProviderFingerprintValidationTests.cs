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

internal static class HotProviderFingerprintValidationTests
{
    public static void RunAll()
    {
        RejectsProjectionDefinitionWhenFingerprintDoesNotMatch();
        RejectsFilterDefinitionWhenFingerprintDoesNotMatch();
    }

    private static void RejectsFilterDefinitionWhenFingerprintDoesNotMatch()
    {
        FilterExpression fingerprintSource = CharacterIdEquals(7);
        FilterExpression embeddedDefinition = CharacterIdEquals(8);
        GeneratorRun run = RunGenerator(
            Manifest(
                "filter",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Fingerprint",
                Fingerprint(fingerprintSource),
                embeddedDefinition),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "mismatched filter fingerprint diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "mismatched filter fingerprint emitted no provider");
    }

    private static void RejectsProjectionDefinitionWhenFingerprintDoesNotMatch()
    {
        EventProjectionExpression fingerprintSource = EventProjectionExpression.Select("CharacterId");
        EventProjectionExpression embeddedDefinition = EventProjectionExpression.Select("EventId");
        GeneratorRun run = RunGenerator(
            Manifest(
                "projection",
                "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Fingerprint",
                ProjectionFingerprint(fingerprintSource),
                embeddedDefinition),
            PluginEventTree());

        AssertDiagnostic(run, "FSFHOT009", "mismatched projection fingerprint diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "mismatched projection fingerprint emitted no provider");
    }

    private static FilterExpression CharacterIdEquals(long value) =>
        FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(value));

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static string ProjectionFingerprint(EventProjectionExpression projection)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Projection.ProjectionExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [projection])!;
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
            "Plugin.Hot.Fingerprint",
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
            "fingerprint.siftql-hot.json",
            JsonSerializer.Serialize(manifest));
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

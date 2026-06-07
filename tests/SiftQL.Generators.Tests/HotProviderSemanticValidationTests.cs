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

internal static class HotProviderSemanticValidationTests
{
    public static void RunAll()
    {
        RejectsCompareWithoutValue();
        RejectsContainsWithoutValue();
        RejectsInWithoutValues();
        RejectsNotWithWrongChildCount();
        RejectsDuplicateProjectionNames();
        RejectsProjectionFieldWithEmptyPath();
        RejectsNonArrayProjectionFields();
        RejectsNonArrayProjectionIncludes();
    }

    private static void RejectsCompareWithoutValue()
    {
        var filter = new FilterExpression(FilterExpressionKind.Compare)
        {
            Field = "CharacterId",
            Operator = FilterOperator.Equal,
        };

        AssertRejected(FilterManifest(filter), "missing compare value diagnostic");
    }

    private static void RejectsContainsWithoutValue()
    {
        var filter = new FilterExpression(FilterExpressionKind.Contains)
        {
            Field = "Tags",
        };

        AssertRejected(FilterManifest(filter), "missing contains value diagnostic");
    }

    private static void RejectsInWithoutValues()
    {
        var filter = new FilterExpression(FilterExpressionKind.In)
        {
            Field = "CharacterId",
            Values = [],
        };

        AssertRejected(FilterManifest(filter), "empty in values diagnostic");
    }

    private static void RejectsNotWithWrongChildCount()
    {
        var filter = new FilterExpression(FilterExpressionKind.Not)
        {
            Children =
            [
                FilterExpression.Exists("CharacterId"),
                FilterExpression.Exists("EventId"),
            ],
        };

        AssertRejected(FilterManifest(filter), "invalid not child count diagnostic");
    }

    private static void RejectsDuplicateProjectionNames()
    {
        var projection = new EventProjectionExpression
        {
            Fields =
            [
                new EventProjectionField("CharacterId", "Duplicate"),
                new EventProjectionField("EventId", "Duplicate"),
            ],
        };

        AssertRejected(ProjectionManifest(projection), "duplicate projection field diagnostic");
    }

    private static void RejectsProjectionFieldWithEmptyPath()
    {
        var projection = new EventProjectionExpression
        {
            Fields = [new EventProjectionField { Name = "Bad", Path = "" }],
        };

        AssertRejected(ProjectionManifest(projection), "empty projection path diagnostic");
    }

    private static void RejectsNonArrayProjectionFields()
    {
        string fingerprint = ProjectionFingerprint(EventProjectionExpression.Default);
        AssertRejected(RawDefinitionManifest("projection", fingerprint, """
            {
              "Fields": 123
            }
            """), "non-array projection fields diagnostic");
    }

    private static void RejectsNonArrayProjectionIncludes()
    {
        string fingerprint = ProjectionFingerprint(EventProjectionExpression.Default);
        AssertRejected(RawDefinitionManifest("projection", fingerprint, """
            {
              "Includes": 123
            }
            """), "non-array projection includes diagnostic");
    }

    private static void AssertRejected(string manifestJson, string label)
    {
        GeneratorRun run = RunGenerator(manifestJson);
        int diagnosticCount = run.Diagnostics.Count(item => item.Id == "FSFHOT009");
        AssertEx.Equal(1, diagnosticCount, label);
        AssertEx.Equal(0, HotProviderSourceCount(run), label + " emitted no hot provider source");
    }

    private static string FilterManifest(FilterExpression filter) =>
        Manifest("filter", Fingerprint(filter), JsonSerializer.SerializeToElement(filter));

    private static string ProjectionManifest(EventProjectionExpression projection) =>
        Manifest("projection", ProjectionFingerprint(projection), JsonSerializer.SerializeToElement(projection));

    private static string RawDefinitionManifest(string kind, string fingerprint, string definitionJson)
    {
        using JsonDocument document = JsonDocument.Parse(definitionJson);
        return Manifest(kind, fingerprint, document.RootElement.Clone());
    }

    private static string Manifest(string kind, string fingerprint, JsonElement definition)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = kind + "|Plugin.Events.PluginOwnedEvent|" + fingerprint,
                    Kind = kind,
                    SubjectType = "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Semantic",
                    Fingerprint = fingerprint,
                    Definition = definition,
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("semantic.fourstory-hot.json", manifestJson)),
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
            "Plugin.Hot.Semantic",
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
                long CharacterId,
                string[] Tags) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string Fingerprint(FilterExpression expression) =>
        InvokeFingerprint("SiftQL.FilterExpressionFingerprint", expression);

    private static string ProjectionFingerprint(EventProjectionExpression projection) =>
        InvokeFingerprint("SiftQL.Projection.ProjectionExpressionFingerprint", projection);

    private static string InvokeFingerprint(string typeName, object expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(typeName, throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

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

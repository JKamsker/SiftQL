using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotManifestProjectionValidationTests
{
    [Fact]
    public void RejectsMissingProjectionIncludeIntrinsic() =>
        AssertRejectedInclude("""
            {
              "ResultName": "nearby",
              "Arguments": []
            }
            """,
            new EventProjectionInclude { ResultName = "nearby", Arguments = [] },
            "missing projection include intrinsic");

    [Fact]
    public void RejectsMissingProjectionIncludeResultName() =>
        AssertRejectedInclude("""
            {
              "Intrinsic": "server.players.near",
              "Arguments": []
            }
            """,
            new EventProjectionInclude { Intrinsic = "server.players.near", Arguments = [] },
            "missing projection include result name");

    [Fact]
    public void RejectsNonArrayProjectionIncludeArguments() =>
        AssertRejectedInclude("""
            {
              "Intrinsic": "server.players.near",
              "ResultName": "nearby",
              "Arguments": 123
            }
            """,
            new EventProjectionInclude("server.players.near", "nearby"),
            "non-array projection include arguments");

    [Fact]
    public void RejectsUnnamedProjectionIncludeArguments() =>
        AssertRejectedInclude("""
            {
              "Intrinsic": "server.players.near",
              "ResultName": "nearby",
              "Arguments": [
                { "Name": "", "Value": { "Kind": 2, "Integer": 5 } }
              ]
            }
            """,
            new EventProjectionInclude
            {
                Intrinsic = "server.players.near",
                ResultName = "nearby",
                Arguments =
                [
                    new EventProjectionArgument { Name = "", Value = FilterValue.From(5L) },
                ],
            },
            "unnamed projection include argument");

    [Fact]
    public void RejectsDuplicateProjectionIncludeArguments() =>
        AssertRejectedInclude("""
            {
              "Intrinsic": "server.players.near",
              "ResultName": "nearby",
              "Arguments": [
                { "Name": "limit", "Value": { "Kind": 2, "Integer": 5 } },
                { "Name": "limit", "Value": { "Kind": 2, "Integer": 10 } }
              ]
            }
            """,
            new EventProjectionInclude
            {
                Intrinsic = "server.players.near",
                ResultName = "nearby",
                Arguments =
                [
                    new EventProjectionArgument("limit", FilterValue.From(5L)),
                    new EventProjectionArgument("limit", FilterValue.From(10L)),
                ],
            },
            "duplicate projection include argument");

    [Fact]
    public void RejectsDuplicateProjectionIncludeResultNames() =>
        AssertRejectedProjection("""
            [
              {
                "Intrinsic": "client.player",
                "ResultName": "player",
                "Arguments": []
              },
              {
                "Intrinsic": "client.target",
                "ResultName": "player",
                "Arguments": []
              }
            ]
            """,
            EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude("client.player", "player"),
                new EventProjectionInclude("client.target", "player"),
            ]),
            "duplicate projection include result names");

    [Fact]
    public void RejectsProjectionFieldLimit()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default.WithFields(
            Enumerable.Range(0, 65)
                .Select(static index => new EventProjectionField("CharacterId", "field" + index))
                .ToArray());

        AssertRejectedProjectionDefinition(projection, "projection field limit");
    }

    [Fact]
    public void RejectsProjectionIncludeLimit()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default.WithIncludes(
            Enumerable.Range(0, 9)
                .Select(static index => new EventProjectionInclude("include." + index, "result" + index))
                .ToArray());

        AssertRejectedProjectionDefinition(projection, "projection include limit");
    }

    private static void AssertRejectedInclude(
        string includeJson,
        EventProjectionInclude include,
        string label) =>
        AssertRejectedProjection(
            "[" + includeJson + "]",
            EventProjectionExpression.Default.WithIncludes([include]),
            label);

    private static void AssertRejectedProjection(
        string includesJson,
        EventProjectionExpression projection,
        string label)
    {
        GeneratorRun run = RunGenerator(ManifestWithIncludes(includesJson, ProjectionFingerprint(projection)));

        AssertDiagnostic(run, "FSFHOT009", label);
        AssertEx.Equal(0, run.Diagnostics.Count(static item => item.Id == "CS8785"), label + " no generator exception");
        AssertEx.Equal(0, HotProviderSourceCount(run), label + " emitted no hot provider source");
    }

    private static void AssertRejectedProjectionDefinition(
        EventProjectionExpression projection,
        string label)
    {
        GeneratorRun run = RunGenerator(ManifestWithProjection(
            JsonSerializer.Serialize(projection),
            ProjectionFingerprint(projection)));

        AssertDiagnostic(run, "FSFHOT009", label);
        AssertEx.Equal(0, HotProviderSourceCount(run), label + " emitted no hot provider source");
    }

    private static string ManifestWithIncludes(string includesJson, string fingerprint) =>
        ManifestWithProjection(
            $$"""
            {
              "Fields": [],
              "Includes": {{includesJson}}
            }
            """,
            fingerprint);

    private static string ManifestWithProjection(string projectionJson, string fingerprint) =>
        $$"""
        {
          "Schema": "siftql.hot.v1",
          "RuntimeVersion": "10.0.0",
          "FilterEngineVersion": "tiered-v1",
          "GeneratorVersion": "hot-sourcegen-v1",
          "Entries": [
            {
              "Kind": "projection",
              "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.ProjectionValidation",
              "Fingerprint": "{{fingerprint}}",
              "Definition": {{projectionJson}}
            }
          ]
        }
        """;

    private static void AssertDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(1, count, label);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static string ProjectionFingerprint(EventProjectionExpression projection)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Projection.ProjectionExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [projection])!;
    }

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("projection-validation.siftql-hot.json", manifestJson)),
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
            "Plugin.Hot.ProjectionValidation",
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

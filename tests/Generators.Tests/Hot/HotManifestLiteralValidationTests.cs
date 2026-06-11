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

public sealed class HotManifestLiteralValidationTests
{
    [Fact]
    public void RejectsNonFiniteHotNumbers()
    {
        GeneratorRun run = RunGenerator(RawManifest("""
            {
              "Kind": "filter",
              "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Literals",
              "Fingerprint": "non-finite-number",
              "Definition": {
                "Kind": 4,
                "Field": "Amount",
                "Operator": 0,
                "Value": { "Kind": 3, "Number": 1e309 }
              }
            }
            """));

        AssertDiagnostic(run, "FSFHOT009", "non-finite number diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "non-finite number emitted no provider");
    }

    [Fact]
    public void RejectsMalformedBooleanLiteral()
    {
        var falseFilter = FilterExpression.Compare(
            "Enabled",
            FilterOperator.Equal,
            FilterValue.From(false));
        GeneratorRun run = RunGenerator(RawManifest($$"""
            {
              "Kind": "filter",
              "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Literals",
              "Fingerprint": "{{Fingerprint(falseFilter)}}",
              "Definition": {
                "Kind": 4,
                "Field": "Enabled",
                "Operator": 0,
                "Value": { "Kind": 1, "Boolean": "not-a-bool" }
              }
            }
            """));

        AssertDiagnostic(run, "FSFHOT009", "malformed boolean literal diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "malformed boolean emitted no provider");
    }

    [Fact]
    public void RejectsIncompatibleHotFilterLiteral()
    {
        var filter = FilterExpression.Compare(
            "Enabled",
            FilterOperator.Equal,
            new FilterValue { Kind = FilterValueKind.String, String = "true" });
        GeneratorRun run = RunGenerator(Manifest(filter));

        AssertDiagnostic(run, "FSFHOT009", "incompatible filter literal diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "incompatible filter literal emitted no provider");
    }

    [Fact]
    public void RejectsOrderedComparisonOnStringField()
    {
        var filter = FilterExpression.Compare(
            "Source",
            FilterOperator.GreaterThan,
            FilterValue.From("m"));
        GeneratorRun run = RunGenerator(Manifest(filter));

        AssertDiagnostic(run, "FSFHOT009", "ordered string filter diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "ordered string filter emitted no provider");
    }

    [Fact]
    public void RejectsOrderedComparisonAgainstNull()
    {
        var filter = FilterExpression.Compare(
            "Amount",
            FilterOperator.GreaterThan,
            FilterValue.Null);
        GeneratorRun run = RunGenerator(Manifest(filter));

        AssertDiagnostic(run, "FSFHOT009", "ordered null filter diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "ordered null filter emitted no provider");
    }

    [Fact]
    public void RejectsMalformedIgnoreCaseFlag()
    {
        var filter = FilterExpression.Compare(
            "Source",
            FilterOperator.Equal,
            FilterValue.From("alpha"));
        GeneratorRun run = RunGenerator(RawManifest($$"""
            {
              "Kind": "filter",
              "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Literals",
              "Fingerprint": "{{Fingerprint(filter)}}",
              "Definition": {
                "Kind": 4,
                "Field": "Source",
                "Operator": 0,
                "IgnoreCase": "not-bool",
                "Value": { "Kind": 4, "String": "alpha" }
              }
            }
            """));

        AssertDiagnostic(run, "FSFHOT009", "malformed ignoreCase diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "malformed ignoreCase emitted no provider");
    }

    [Fact]
    public void RejectsMalformedParameterKey()
    {
        var filter = FilterExpression.Compare(
            "Amount",
            FilterOperator.Equal,
            FilterValue.From(1D));
        GeneratorRun run = RunGenerator(RawManifest($$"""
            {
              "Kind": "filter",
              "SubjectType": "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Literals",
              "Fingerprint": "{{Fingerprint(filter)}}",
              "Definition": {
                "Kind": 4,
                "Field": "Amount",
                "Operator": 0,
                "Value": { "Kind": 3, "ParameterKey": 123, "Number": 1 }
              }
            }
            """));

        AssertDiagnostic(run, "FSFHOT009", "malformed parameter key diagnostic");
        AssertEx.Equal(0, HotProviderSourceCount(run), "malformed parameter key emitted no provider");
    }

    [Fact]
    public void AcceptsStringContainsHotFilter()
    {
        var filter = FilterExpression.StringContains(
            "Source",
            FilterValue.From("ell"));

        GeneratorRun run = RunGenerator(Manifest(filter));

        AssertEx.Equal(0, run.Diagnostics.Length, "string contains generator diagnostics");
        AssertEx.Equal(1, HotProviderSourceCount(run), "string contains emitted a provider");
        AssertNoCompilationErrors(run, "string contains hot provider");
    }

    [Fact]
    public void EscapesUnicodeSeparatorsInStringLiterals()
    {
        string value = "line\u2028paragraph\u2029";
        var filter = FilterExpression.Compare(
            "Source",
            FilterOperator.Equal,
            FilterValue.From(value));
        GeneratorRun run = RunGenerator(Manifest(filter));

        AssertEx.Equal(0, run.Diagnostics.Length, "unicode literal generator diagnostics");
        AssertNoCompilationErrors(run, "unicode literal hot provider");
    }

    private static string RawManifest(string entryJson) =>
        $$"""
        {
          "Schema": "siftql.hot.v1",
          "RuntimeVersion": "10.0.0",
          "FilterEngineVersion": "tiered-v1",
          "GeneratorVersion": "hot-sourcegen-v1",
          "Entries": [{{entryJson}}]
        }
        """;

    private static string Manifest(FilterExpression filter)
    {
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PluginOwnedEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, Plugin.Hot.Literals",
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("literal.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
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
            "Plugin.Hot.Literals",
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
                double Amount,
                string Source,
                bool Enabled) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AssertDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(1, count, label);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
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

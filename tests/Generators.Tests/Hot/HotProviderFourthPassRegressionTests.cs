using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderFourthPassRegressionTests
{
    [Fact]
    public void GeneratorResolvesClosedGenericArraySubjectFromAssemblyQualifiedManifest()
    {
        const string assemblyName = "Plugin.Hot.GenericArray";
        var filter = FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        string fingerprint = Fingerprint(filter);
        string subjectType = "Plugin.Events.GenericEvent`1[[" +
            typeof(int[]).AssemblyQualifiedName +
            "]], " +
            assemblyName;
        string manifestJson = ManifestJson("filter", subjectType, fingerprint, filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("generic-array.siftql-hot.json", manifestJson),
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record GenericEvent<T>(
                    Guid EventId,
                    long ItemId) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        AssertNoDiagnostic(run, "FSFHOT009", "closed generic array subject resolved");
        AssertEx.Equal(1, HotProviderSourceCount(run), "closed generic array provider source emitted");
        AssertNoCompilationErrors(run, "closed generic array hot provider");
    }

    [Fact]
    public void HotFilterFingerprintAcceptsRuntimeCanonicalNegativeZero()
    {
        var filter = FilterExpression.Compare(
            "Score",
            FilterOperator.Equal,
            FilterValue.From(-0.0D));
        GeneratorRun run = RunGenerator(
            "Plugin.Hot.NumericFingerprint",
            new InMemoryAdditionalText(
                "negative-zero.siftql-hot.json",
                ManifestJson(
                    "filter",
                    "Plugin.Events.PluginOwnedEvent, Plugin.Hot.NumericFingerprint",
                    Fingerprint(filter),
                    filter)),
            PluginEventTree());

        AssertNoDiagnostic(run, "FSFHOT009", "negative-zero filter fingerprint accepted");
    }

    [Fact]
    public void HotFilterFingerprintAcceptsRuntimeCanonicalDecimalScale()
    {
        var filter = FilterExpression.Compare(
            "Amount",
            FilterOperator.Equal,
            FilterValue.From(1.10m));
        GeneratorRun run = RunGenerator(
            "Plugin.Hot.DecimalFingerprint",
            new InMemoryAdditionalText(
                "decimal.siftql-hot.json",
                ManifestJson(
                    "filter",
                    "Plugin.Events.PluginOwnedEvent, Plugin.Hot.DecimalFingerprint",
                    Fingerprint(filter),
                    filter)),
            PluginEventTree());

        AssertNoDiagnostic(run, "FSFHOT009", "decimal filter fingerprint accepted");
    }

    [Fact]
    public void HotProjectionFingerprintAcceptsDoubleIncludeArgument()
    {
        EventProjectionExpression projection = EventProjectionExpression
            .Select("Score")
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "test.window",
                    "window",
                    [new EventProjectionArgument("scale", FilterValue.From(0.5D))]),
            ]);
        GeneratorRun run = RunGenerator(
            "Plugin.Hot.ProjectionFingerprint",
            new InMemoryAdditionalText(
                "projection.siftql-hot.json",
                ManifestJson(
                    "projection",
                    "Plugin.Events.PluginOwnedEvent, Plugin.Hot.ProjectionFingerprint",
                    ProjectionFingerprint(projection),
                    projection)),
            PluginEventTree());

        AssertNoDiagnostic(run, "FSFHOT009", "projection include fingerprint accepted");
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, params SyntaxTree[] extraTrees)
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
            extraTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string ManifestJson(
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
        return JsonSerializer.Serialize(manifest);
    }

    private static SyntaxTree PluginEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PluginOwnedEvent(
                Guid EventId,
                double Score,
                decimal Amount) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string Fingerprint(FilterExpression expression) =>
        InvokeFingerprint("SiftQL.Compiler.FilterExpressionFingerprint", expression);

    private static string ProjectionFingerprint(EventProjectionExpression projection) =>
        InvokeFingerprint("SiftQL.Projection.ProjectionExpressionFingerprint", projection);

    private static string InvokeFingerprint(string typeName, object expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(typeName, throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic)!.Invoke(null, [expression])!;
    }

    private static void AssertNoDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(0, count, label);
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

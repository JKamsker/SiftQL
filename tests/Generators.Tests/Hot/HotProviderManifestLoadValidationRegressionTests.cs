using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderManifestLoadValidationRegressionTests
{
    [Fact]
    public void LoaderAcceptsManifestKindCasingAcceptedByGenerator()
    {
        const string assemblyName = "Plugin.Hot.KindCasing";
        FilterExpression filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(7L));
        string manifestJson = ManifestJson(
            assemblyName,
            "Filter",
            "Plugin.Events.KindCasingEvent, " + assemblyName,
            FilterExpressionFingerprint.Create(filter),
            filter);
        Compilation output = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("kind.siftql-hot.json", manifestJson),
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record KindCasingEvent(long Value) : IFilterSubject;
                """));

        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult result = Load(output, assemblyName, manifestJson);

        Assert.True(result.Loaded, result.Message);
    }

    [Fact]
    public void LoaderResolvesClosedGenericManifestSubjectsFromLoadContext()
    {
        const string assemblyName = "Plugin.Hot.GenericLoader";
        FilterExpression filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(7L));
        string subjectType = "Plugin.Events.GenericEvent`1[[" +
            typeof(int).AssemblyQualifiedName +
            "]], " +
            assemblyName;
        string manifestJson = ManifestJson(
            assemblyName,
            "filter",
            subjectType,
            FilterExpressionFingerprint.Create(filter),
            filter);
        Compilation output = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("generic.siftql-hot.json", manifestJson),
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record GenericEvent<T>(long Value) : IFilterSubject;
                """));

        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult result = Load(output, assemblyName, manifestJson);

        Assert.True(result.Loaded, result.Message);
    }

    private static Compilation RunGenerator(
        string assemblyName,
        AdditionalText manifest,
        params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, trees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(manifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);
        Assert.Empty(diagnostics);
        AssertNoCompilationErrors(output);
        return output;
    }

    private static HotTieredProviderLoadResult Load(
        Compilation output,
        string assemblyName,
        string manifestJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotLoadValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = output.Emit(assemblyPath);
        Assert.True(emit.Success, string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);
        return HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
    }

    private static string ManifestJson(
        string assemblyName,
        string kind,
        string subjectType,
        string fingerprint,
        FilterExpression filter)
    {
        _ = assemblyName;
        return JsonSerializer.Serialize(new HotCompilationManifest
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
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        });
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AssertNoCompilationErrors(Compilation output)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}

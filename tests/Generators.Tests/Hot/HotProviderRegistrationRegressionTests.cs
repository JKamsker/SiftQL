using System.Collections.Immutable;
using System.Text;
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

public sealed class HotProviderRegistrationRegressionTests
{
    [Fact]
    public void LoadingOneSameContentManifestRegistersOnlyOneProvider()
    {
        const string assemblyName = "Plugin.Hot.SameContentManifest";
        string manifest = ManifestJson(FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L)));
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("alpha/same.siftql-hot.json", manifest),
            new InMemoryAdditionalText("beta/same.siftql-hot.json", manifest));
        AssertEx.Equal(0, run.Diagnostics.Length, "same-content manifest diagnostics");

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotSameContent", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "same.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "same-content hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifest);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        int beforeLoad = PrecompiledTieredProviderRegistry.GlobalVersion;
        using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        AssertEx.True(result.Loaded, "same-content manifest loaded: " + result.Message);
        Assert.Equal(beforeLoad + 1, PrecompiledTieredProviderRegistry.GlobalVersion);
    }

    [Fact]
    public void MetadataOnlyAssemblyDoesNotReportLoadedProvider()
    {
        var manifest = new HotCompilationManifest { RuntimeVersion = "10.0.0" };
        string manifestJson = JsonSerializer.Serialize(manifest);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        SyntaxTree metadataOnly = CSharpSyntaxTree.ParseText($$"""
            [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestHash", "{{manifestHash}}")]
            [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestSchema", "siftql.hot.v1")]
            [assembly: System.Reflection.AssemblyMetadata("SiftQLHotFilterEngine", "tiered-v1")]
            [assembly: System.Reflection.AssemblyMetadata("SiftQLHotGenerator", "hot-sourcegen-v1")]
            namespace Plugin.Hot;
            public static class MetadataOnly { }
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            "Plugin.Hot.MetadataOnly",
            metadataOnly);

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotMetadataOnly", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Plugin.Hot.MetadataOnly.dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "metadata-only hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.False(result.Loaded);
    }

    private static GeneratorRun RunGenerator(string assemblyName, params AdditionalText[] manifests)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: manifests.ToImmutableArray(),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(outputCompilation, diagnostics);
    }

    private static string ManifestJson(FilterExpression filter)
    {
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + typeof(ItemUsedEvent).FullName + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
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
        return (string)type.GetMethod("Create", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [expression])!;
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics);
}

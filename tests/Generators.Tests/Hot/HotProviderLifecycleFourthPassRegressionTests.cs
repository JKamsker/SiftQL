using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderLifecycleFourthPassRegressionTests
{
    [Fact]
    public void LoadedProviderDisposesOwningScopeWhenNestedScopeIsCurrent()
    {
        const string assemblyName = "Plugin.Hot.NestedScopeDispose";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(24680L));
        string manifestJson = ItemUsedManifestJson(filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("nested-scope.siftql-hot.json", manifestJson));
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotNestedScopeDispose",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "nested-scope hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        using var outer = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        try
        {
            AssertEx.True(result.Loaded, "nested-scope provider loaded: " + result.Message);
            Assert.False(CompileTiered(filter).IsTiered);

            using (PrecompiledTieredProviderRegistry.CreateIsolatedScope())
                result.Dispose();

            Assert.True(CompileTiered(filter).IsTiered);
        }
        finally
        {
            result.Dispose();
        }
    }

    private static CompiledKernel CompileTiered(FilterExpression filter) =>
        FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered);

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        _ = diagnostics;
        return new(outputCompilation);
    }

    private static string ItemUsedManifestJson(FilterExpression filter)
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

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

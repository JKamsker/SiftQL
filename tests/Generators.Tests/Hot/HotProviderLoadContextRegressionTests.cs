using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL;
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
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderLoadContextRegressionTests
{
    [Fact]
    public void LoaderUsesHostRuntimeWhenProviderDirectoryContainsRuntimeCopies()
    {
        const string assemblyName = "Plugin.Hot.PrivateRuntime";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(777L));
        string manifestJson = HotManifestJson(filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("private-runtime.siftql-hot.json", manifestJson));

        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotPrivateRuntime",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "private-runtime hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);
        CopyRuntimeAssembly(typeof(FilterCompiler), directory);
        CopyRuntimeAssembly(typeof(IFilterSubject), directory);

        try
        {
            using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            AssertEx.True(result.Loaded, "loader shared host runtime assemblies: " + result.Message);
            CompiledKernel kernel = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered);
            AssertEx.True(!kernel.IsTiered, "private-runtime provider registered with host registry");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static GeneratorRun RunGenerator(string assemblyName, AdditionalText hotManifest)
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
        Assert.Empty(diagnostics);
        return new(outputCompilation);
    }

    private static string HotManifestJson(FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string subject = typeof(ItemUsedEvent).AssemblyQualifiedName!;
        return JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + subject + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = subject,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        });
    }

    private static void CopyRuntimeAssembly(Type type, string directory)
    {
        string source = type.Assembly.Location;
        File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Best-effort cleanup; collectible contexts may release files asynchronously.
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

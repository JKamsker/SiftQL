using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Compiler;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Values;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderLoaderManifestIsolationTests
{
    [Fact]
    public void LoadingOneManifestFromSharedAssemblyDoesNotRegisterOtherManifestProviders()
    {
        const string assemblyName = "Plugin.Hot.ManifestIsolation";
        FilterExpression firstFilter = Filter(nameof(ItemUsedEvent.ItemId), 100);
        FilterExpression secondFilter = Filter(nameof(ItemUsedEvent.Quantity), 2);
        string firstManifest = ManifestJson(firstFilter);
        string secondManifest = ManifestJson(secondFilter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("first.siftql-hot.json", firstManifest),
            new InMemoryAdditionalText("second.siftql-hot.json", secondManifest));

        AssertEx.Equal(0, run.Diagnostics.Length, "manifest isolation diagnostics");
        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotManifestIsolation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string firstManifestPath = Path.Combine(directory, "first.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "manifest isolation provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(firstManifestPath, firstManifest);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = firstManifestPath,
            RequireExactRuntimeVersion = false,
        });
        AssertEx.True(result.Loaded, "first manifest loaded: " + result.Message);

        CompiledKernel first = FilterCompiler.Compile(typeof(ItemUsedEvent), firstFilter, FilterCompilerOptions.Tiered);
        CompiledKernel second = FilterCompiler.Compile(typeof(ItemUsedEvent), secondFilter, FilterCompilerOptions.Tiered);

        AssertEx.True(!first.IsTiered, "loaded manifest provider was registered");
        AssertEx.True(second.IsTiered, "unloaded manifest provider was not registered");
    }

    [Fact]
    public void GeneratedRegistrationChecksManifestBeforeProviderConstruction()
    {
        const string assemblyName = "Plugin.Hot.ManifestIsolation.Source";
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText(
                "first.siftql-hot.json",
                ManifestJson(Filter(nameof(ItemUsedEvent.ItemId), 100))));

        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();

        AssertEx.Contains("RegisterFactory(", source, "registration is factory-gated before provider construction");
        AssertEx.True(
            !source.Contains("Register(new ", StringComparison.Ordinal),
            "registration does not construct providers before manifest gate evaluation");
    }

    private static FilterExpression Filter(string field, long value) =>
        FilterExpression.Compare(
            field,
            FilterOperator.Equal,
            FilterValue.From(value));

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
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
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

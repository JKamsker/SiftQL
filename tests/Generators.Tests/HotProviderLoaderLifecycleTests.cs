using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
// removed: source-specific events
using SiftQL;
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

internal static class HotProviderLoaderLifecycleTests
{
    public static void RunAll()
    {
        LoadedProviderCanBeDisposedAndUnregistered();
        TelemetryOnlyManifestChangesDoNotInvalidateGeneratedProvider();
        MalformedManifestReportsInvalidManifest();
        MultipleManifestsInOneAssemblyEachValidateOwnHash();
    }

    private static void LoadedProviderCanBeDisposedAndUnregistered()
    {
        const string assemblyName = "Plugin.Hot.Lifecycle";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(987654321));
        string manifestJson = HotManifestJson(filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("lifecycle.siftql-hot.json", manifestJson));

        AssertEx.Equal(0, run.Diagnostics.Length, "lifecycle generator diagnostics");

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotProviderLifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "lifecycle hot provider assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
        AssertEx.True(result.Loaded, "lifecycle provider loaded: " + result.Message);

        var hot = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Tiered);
        AssertEx.True(!hot.IsTiered, "loaded provider supplied precompiled filter");
        object loadedResult = result;
        AssertEx.True(loadedResult is IDisposable, "loaded provider exposes a disposal handle");

        ((IDisposable)loadedResult).Dispose();

        var afterDispose = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Tiered);
        AssertEx.True(afterDispose.IsTiered, "disposed provider was unregistered");
    }

    private static void TelemetryOnlyManifestChangesDoNotInvalidateGeneratedProvider()
    {
        const string assemblyName = "Plugin.Hot.Telemetry";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(123456789));
        string manifestJson = HotManifestJson(filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("telemetry.siftql-hot.json", manifestJson));

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotProviderTelemetry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "telemetry hot provider assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, MutateTelemetryOnly(manifestJson));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        AssertEx.True(result.Loaded, "telemetry-only manifest changes do not invalidate provider: " + result.Message);
    }

    private static void MalformedManifestReportsInvalidManifest()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotProviderMalformed",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, "hot.json");
        string assemblyPath = Path.Combine(directory, "hot.dll");
        File.WriteAllText(manifestPath, "{ broken json");
        File.WriteAllBytes(assemblyPath, []);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        AssertEx.Equal(
            HotTieredProviderLoadStatus.InvalidManifest,
            result.Status,
            "malformed hot provider manifest reports invalid manifest");
    }

    private static void MultipleManifestsInOneAssemblyEachValidateOwnHash()
    {
        const string assemblyName = "Plugin.Hot.Multiple";
        FilterExpression firstFilter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        FilterExpression secondFilter = FilterExpression.Compare(
            nameof(ItemUsedEvent.Quantity),
            FilterOperator.Equal,
            FilterValue.From(2L));
        string firstManifestJson = HotManifestJson(firstFilter);
        string secondManifestJson = HotManifestJson(secondFilter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("first.siftql-hot.json", firstManifestJson),
            new InMemoryAdditionalText("second.siftql-hot.json", secondManifestJson));

        AssertEx.Equal(0, run.Diagnostics.Length, "multi-manifest generator diagnostics");

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotProviderMultiple", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string firstManifestPath = Path.Combine(directory, "first.json");
        string secondManifestPath = Path.Combine(directory, "second.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "multi-manifest hot provider assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(firstManifestPath, firstManifestJson);
        File.WriteAllText(secondManifestPath, secondManifestJson);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult first = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = firstManifestPath,
            RequireExactRuntimeVersion = false,
        });
        using HotTieredProviderLoadResult second = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = secondManifestPath,
            RequireExactRuntimeVersion = false,
        });

        AssertEx.True(first.Loaded, "first manifest loaded from shared DLL: " + first.Message);
        AssertEx.True(second.Loaded, "second manifest loaded from shared DLL: " + second.Message);
    }

    private static GeneratorRun RunGenerator(string assemblyName, params AdditionalText[] hotManifests)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: hotManifests.ToImmutableArray(),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterCompiler).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string HotManifestJson(FilterExpression filter)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + typeof(ItemUsedEvent).FullName + "|" + Fingerprint(filter),
                    Kind = "filter",
                    SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
                    Fingerprint = Fingerprint(filter),
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string MutateTelemetryOnly(string manifestJson)
    {
        HotCompilationManifest manifest = JsonSerializer.Deserialize<HotCompilationManifest>(manifestJson)!;
        return JsonSerializer.Serialize(manifest with
        {
            GeneratedAtUtc = manifest.GeneratedAtUtc.AddMinutes(5),
            Entries = manifest.Entries
                .Select(static entry => entry with
                {
                    Observed = entry.Observed with
                    {
                        Evaluations = entry.Observed.Evaluations + 99,
                        Matches = entry.Observed.Matches + 3,
                        LastSeenUtc = DateTimeOffset.UtcNow,
                    },
                })
                .ToArray(),
        });
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics);
}

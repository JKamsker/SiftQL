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
    public void SuccessfulLoadDoesNotKeepDirectProviderRegistrationsFromLoadedAssembly()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.ManifestDirectIsolation";
        FilterExpression manifestFilter = Filter(nameof(ItemUsedEvent.ItemId), 100);
        FilterExpression directFilter = Filter(nameof(ItemUsedEvent.Quantity), 2);
        string manifestJson = ManifestJson(manifestFilter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            assemblyName,
            Source(DirectRegistrationSource(
                manifestHash,
                Fingerprint(manifestFilter),
                Fingerprint(directFilter))));

        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotManifestDirectIsolation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "manifest direct-isolation provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        try
        {
            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            AssertEx.True(result.Loaded, "manifest direct-isolation provider loaded: " + result.Message);
            CompiledKernel manifest = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                manifestFilter,
                FilterCompilerOptions.Tiered);
            CompiledKernel direct = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                directFilter,
                FilterCompilerOptions.Tiered);

            AssertEx.True(!manifest.IsTiered, "manifest-scoped provider was registered");
            AssertEx.True(direct.IsTiered, "direct provider from loaded assembly was quarantined");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
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

    private static string DirectRegistrationSource(
        string manifestHash,
        string manifestFingerprint,
        string directFingerprint) =>
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using SiftQL.Hot;
        using SiftQL.Projected;

        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestHash", "{{manifestHash}}")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestSchema", "siftql.hot.v1")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotFilterEngine", "tiered-v1")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotGenerator", "hot-sourcegen-v1")]

        namespace Plugin.Hot;

        internal static class DirectRegistration
        {
            [ModuleInitializer]
            internal static void Register()
            {
                HotProviderRegistrationContext.RegisterFactory(
                    static () => new ManifestProvider(),
                    "{{manifestHash}}");
                PrecompiledTieredProviderRegistry.Register(new DirectProvider());
            }
        }

        internal sealed class ManifestProvider : Provider
        {
            public ManifestProvider() : base("{{manifestFingerprint}}") { }
        }

        internal sealed class DirectProvider : Provider
        {
            public DirectProvider() : base("{{directFingerprint}}") { }
        }

        internal abstract class Provider(string acceptedFingerprint) : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                if (string.Equals(subjectType.FullName, "SiftQL.Generators.Tests.ItemUsedEvent", StringComparison.Ordinal) &&
                    string.Equals(key, acceptedFingerprint, StringComparison.Ordinal))
                {
                    predicate = static _ => true;
                    return true;
                }

                predicate = null;
                return false;
            }

            public bool TryGetProjection(
                Type subjectType,
                string key,
                out Func<object, ProjectedEventField[]>? projectFields)
            {
                _ = subjectType;
                _ = key;
                projectFields = null;
                return false;
            }
        }
        """;

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

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Best-effort cleanup; loaded assemblies can briefly keep files open on Windows.
        }
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

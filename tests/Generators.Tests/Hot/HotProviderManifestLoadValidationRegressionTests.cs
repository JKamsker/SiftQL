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
        WithLoadedProvider(output, assemblyName, manifestJson, static result =>
            Assert.True(result.Loaded, result.Message));
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
        WithLoadedProvider(output, assemblyName, manifestJson, static result =>
            Assert.True(result.Loaded, result.Message));
    }

    [Fact]
    public void LoaderAcceptsRuntimeVersionSkewWhenExactRuntimeVersionIsNotRequired()
    {
        const string assemblyName = "Plugin.Hot.RuntimeSkew";
        FilterExpression filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(7L));
        string subjectType = "Plugin.Events.RuntimeSkewEvent, " + assemblyName;
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string generatorManifest = ManifestJson(
            assemblyName,
            "filter",
            subjectType,
            fingerprint,
            filter,
            runtimeVersion: "10.0.0");
        string runtimeManifest = ManifestJson(
            assemblyName,
            "filter",
            subjectType,
            fingerprint,
            filter,
            runtimeVersion: "11.0.0");
        Compilation output = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("runtime-skew.siftql-hot.json", generatorManifest),
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record RuntimeSkewEvent(long Value) : IFilterSubject;
                """));

        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        WithLoadedProvider(output, assemblyName, runtimeManifest, static result =>
            Assert.True(result.Loaded, result.Message));
    }

    [Fact]
    public void LoaderRejectsAssemblyQualifiedSubjectFromDifferentAssembly()
    {
        const string assemblyName = "Plugin.Hot.Current";
        FilterExpression filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(7L));
        string subjectType = "Plugin.Events.AssemblySkewEvent, Plugin.Hot.Old";
        string manifestJson = ManifestJson(
            assemblyName,
            "filter",
            subjectType,
            FilterExpressionFingerprint.Create(filter),
            filter);
        Compilation output = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("assembly-skew.siftql-hot.json", manifestJson),
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record AssemblySkewEvent(long Value) : IFilterSubject;
                """));

        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        WithLoadedProvider(output, assemblyName, manifestJson, static result =>
            Assert.False(result.Loaded, result.Message));
    }

    [Fact]
    public void LoaderRejectsUnsupportedHotFilterDefinitionEvenWhenProviderSatisfiesFingerprint()
    {
        const string assemblyName = "Plugin.Hot.UnsupportedClaim";
        const string eventTypeName = "Plugin.Events.UnsupportedClaimEvent";
        FilterExpression filter = FilterExpression.Count(
            "Values",
            FilterOperator.GreaterThan,
            FilterValue.From(0L));
        string subjectType = eventTypeName + ", " + assemblyName;
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestJson = ManifestJson(
            assemblyName,
            "filter",
            subjectType,
            fingerprint,
            filter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        Compilation output = GeneratorTestCompilation.Create(
            assemblyName,
            Source(ClaimingProviderSource(manifestHash, fingerprint)));
        AssertNoCompilationErrors(output);

        (HotTieredProviderLoadResult result, string directory) = Load(output, assemblyName, manifestJson);
        try
        {
            using (result)
                Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
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

    private static void WithLoadedProvider(
        Compilation output,
        string assemblyName,
        string manifestJson,
        Action<HotTieredProviderLoadResult> assert)
    {
        (HotTieredProviderLoadResult result, string directory) = Load(output, assemblyName, manifestJson);
        try
        {
            using (result)
                assert(result);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static (HotTieredProviderLoadResult Result, string Directory) Load(
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
        return (
            HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            }),
            directory);
    }

    private static string ManifestJson(
        string assemblyName,
        string kind,
        string subjectType,
        string fingerprint,
        FilterExpression filter,
        string runtimeVersion = "10.0.0")
    {
        _ = assemblyName;
        return JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = runtimeVersion,
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

    private static string ClaimingProviderSource(string manifestHash, string fingerprint) =>
        $$"""
        using System;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using SiftQL;
        using SiftQL.Expressions;
        using SiftQL.Hot;
        using SiftQL.Projected;

        [assembly: AssemblyMetadata("SiftQLHotManifestHash", "{{manifestHash}}")]
        [assembly: AssemblyMetadata("SiftQLHotManifestSchema", "{{HotCompilationManifestCompatibility.Schema}}")]
        [assembly: AssemblyMetadata("SiftQLHotFilterEngine", "{{HotCompilationManifestCompatibility.Engine}}")]
        [assembly: AssemblyMetadata("SiftQLHotGenerator", "{{HotCompilationManifestCompatibility.Generator}}")]

        namespace Plugin.Events;

        public sealed record UnsupportedClaimEvent(long[] Values) : IFilterSubject;

        internal sealed class ClaimingProvider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(
                Type subjectType,
                string fingerprint,
                out Func<object, bool>? predicate)
            {
                if (subjectType == typeof(UnsupportedClaimEvent) &&
                    string.Equals(fingerprint, "{{fingerprint}}", StringComparison.Ordinal))
                {
                    predicate = static _ => true;
                    return true;
                }

                predicate = null;
                return false;
            }

            public bool TryGetProjection(
                Type subjectType,
                string fingerprint,
                out Func<object, ProjectedEventField[]>? projectFields)
            {
                _ = subjectType;
                _ = fingerprint;
                projectFields = null;
                return false;
            }
        }

        internal static class ProviderRegistration
        {
            [ModuleInitializer]
            internal static void Register() =>
                HotProviderRegistrationContext.Register(new ClaimingProvider(), "{{manifestHash}}");
        }
        """;

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AssertNoCompilationErrors(Compilation output)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Best-effort cleanup; loaded assemblies can briefly keep files open on Windows.
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}

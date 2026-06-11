using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderManifestShapeRegressionTests
{
    [Fact]
    public void LoaderRejectsParameterizedOnlyProviderForLiteralFilterManifest()
    {
        FilterExpression filter = ItemIdFilter(parameterized: false);
        string manifestJson = ManifestJson(filter);
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);

        HotTieredProviderLoadStatus status = LoadStatus(
            "Plugin.Hot.ParameterizedOnlyShape",
            manifestJson,
            ProviderSource(manifestHash, fingerprint, parameterizedOnly: true));

        Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, status);
    }

    [Fact]
    public void LoaderRejectsLiteralOnlyProviderForParameterizedFilterManifest()
    {
        FilterExpression filter = ItemIdFilter(parameterized: true);
        string manifestJson = ManifestJson(filter);
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);

        HotTieredProviderLoadStatus status = LoadStatus(
            "Plugin.Hot.LiteralOnlyShape",
            manifestJson,
            ProviderSource(manifestHash, fingerprint, parameterizedOnly: false));

        Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, status);
    }

    [Fact]
    public void LoaderRejectsNullManifestEntriesAsInvalidManifest()
    {
        string manifestJson = ManifestJsonWithNullEntry();
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);

        HotTieredProviderLoadStatus status = LoadStatus(
            "Plugin.Hot.NullEntryShape",
            manifestJson,
            ProviderSource(manifestHash, "unused", parameterizedOnly: false));

        Assert.Equal(HotTieredProviderLoadStatus.InvalidManifest, status);
    }

    private static FilterExpression ItemIdFilter(bool parameterized) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            parameterized
                ? FilterValue.From(100L) with { ParameterKey = "p0" }
                : FilterValue.From(100L));

    private static string ManifestJson(FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + typeof(ItemUsedEvent).AssemblyQualifiedName + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string ManifestJsonWithNullEntry() =>
        $$"""
        {
          "Schema": "{{HotCompilationManifestCompatibility.Schema}}",
          "RuntimeVersion": "10.0.0",
          "FilterEngineVersion": "{{HotCompilationManifestCompatibility.Engine}}",
          "GeneratorVersion": "{{HotCompilationManifestCompatibility.Generator}}",
          "Entries": [null]
        }
        """;

    private static HotTieredProviderLoadStatus LoadStatus(
        string assemblyName,
        string manifestJson,
        string providerSource)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotProviderShape",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");

        try
        {
            Emit(CreateCompilation(assemblyName, Source(providerSource)), assemblyPath);
            File.WriteAllText(manifestPath, manifestJson);
            using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });
            return result.Status;
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string ProviderSource(
        string manifestHash,
        string fingerprint,
        bool parameterizedOnly) =>
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

        internal static class Registration
        {
            [ModuleInitializer]
            internal static void Register()
            {
                HotProviderRegistrationContext.RegisterFactory(static () => new ShapeProvider(), "{{manifestHash}}");
            }
        }

        internal sealed class ShapeProvider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                if ({{(parameterizedOnly ? "false" : "true")}} &&
                    subjectType.FullName == "{{typeof(ItemUsedEvent).FullName}}" &&
                    string.Equals(key, "{{fingerprint}}", StringComparison.Ordinal))
                {
                    predicate = static _ => true;
                    return true;
                }

                predicate = null;
                return false;
            }

            public bool TryGetParameterizedFilter(
                Type subjectType,
                string key,
                out ParameterizedHotFilterPredicate? predicate)
            {
                if ({{(parameterizedOnly ? "true" : "false")}} &&
                    subjectType.FullName == "{{typeof(ItemUsedEvent).FullName}}" &&
                    string.Equals(key, "{{fingerprint}}", StringComparison.Ordinal))
                {
                    predicate = static (_, _) => true;
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

    private static CSharpCompilation CreateCompilation(string assemblyName, SyntaxTree source)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterCompiler).Assembly.Location);
        AddReference(references, typeof(ItemUsedEvent).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            [source],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void Emit(Compilation compilation, string assemblyPath)
    {
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "provider emitted: " + string.Join(" | ", emit.Diagnostics));
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Collectible load contexts can release files after the assertion completes.
        }
    }
}

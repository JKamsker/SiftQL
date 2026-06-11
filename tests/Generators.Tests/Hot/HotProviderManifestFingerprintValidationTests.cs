using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderManifestFingerprintValidationTests
{
    [Fact]
    public void LoaderRejectsManifestEntryWhoseFingerprintDoesNotMatchDefinition()
    {
        const string assemblyName = "Plugin.Hot.FingerprintParity";
        FilterExpression definition = ItemIdFilter(100);
        string declaredFingerprint = FilterExpressionFingerprint.Create(ItemIdFilter(200));
        string manifestJson = ManifestJson(definition, declaredFingerprint);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        CSharpCompilation compilation = CreateCompilation(
            assemblyName,
            Source(ProviderSource(manifestHash, declaredFingerprint)));
        string directory = TempDirectory();
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");

        try
        {
            Emit(compilation, assemblyPath);
            File.WriteAllText(manifestPath, manifestJson);
            using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();

            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static FilterExpression ItemIdFilter(long itemId) =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(itemId));

    private static string ManifestJson(FilterExpression definition, string fingerprint) =>
        JsonSerializer.Serialize(new HotCompilationManifest
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
                    Definition = JsonSerializer.SerializeToElement(definition),
                },
            ],
        });

    private static string ProviderSource(string manifestHash, string acceptedFingerprint) =>
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
                HotProviderRegistrationContext.RegisterFactory(
                    static () => new Provider(),
                    "{{manifestHash}}");
            }
        }

        internal sealed class Provider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                if (string.Equals(subjectType.FullName, "SiftQL.Generators.Tests.ItemUsedEvent", StringComparison.Ordinal) &&
                    string.Equals(key, "{{acceptedFingerprint}}", StringComparison.Ordinal))
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
        AssertEx.True(emit.Success, "fingerprint parity provider emitted: " + string.Join(" | ", emit.Diagnostics));
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotFingerprintParity",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Best-effort cleanup; loaded assemblies can briefly keep files open on Windows.
        }
    }
}

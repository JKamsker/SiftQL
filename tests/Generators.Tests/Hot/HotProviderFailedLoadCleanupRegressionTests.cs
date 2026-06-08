using System.Reflection;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderFailedLoadCleanupRegressionTests
{
    [Fact]
    public void FailedLoadRemovesDirectProviderRegistrationsFromLoadedAssembly()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.DirectProviderLeak";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestJson = JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
        });
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        CSharpCompilation compilation = CreateCompilation(
            assemblyName,
            Source(DirectProviderSource(manifestHash, fingerprint)));

        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotDirectProviderLeak",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "direct-provider leak assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        try
        {
            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
            CompiledKernel kernel = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered);
            AssertEx.True(kernel.IsTiered, "failed load removed direct provider side effects");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string DirectProviderSource(string manifestHash, string fingerprint) =>
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
                PrecompiledTieredProviderRegistry.Register(new DirectProvider());
            }
        }

        internal sealed class DirectProvider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(
                Type subjectType,
                string key,
                out Func<object, bool>? predicate)
            {
                if (subjectType == typeof(SiftQL.Generators.Tests.ItemUsedEvent) &&
                    string.Equals(key, "{{fingerprint}}", StringComparison.Ordinal))
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
            // Best-effort cleanup; failed loads can briefly keep files open on Windows.
        }
    }
}

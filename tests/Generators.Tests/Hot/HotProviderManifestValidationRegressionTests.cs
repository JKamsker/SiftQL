using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderManifestValidationRegressionTests
{
    [Fact]
    public void BogusManifestScopedProviderDoesNotSatisfyLoad()
    {
        const string assemblyName = "Plugin.Hot.NoOpManifestProvider";
        FilterExpression filter = ItemIdFilter();
        string manifestJson = ManifestJson(filter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        CSharpCompilation compilation = CreateCompilation(
            assemblyName,
            Source(NoOpProviderSource(manifestHash)));
        string directory = TempDirectory("SiftQLHotNoOpManifestProvider");
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");

        try
        {
            Emit(compilation, assemblyPath, "no-op manifest provider");
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

    [Fact]
    public void FailedLoadRemovesDirectProviderRegistrationsFromDependencyAssembly()
    {
        const string providerAssemblyName = "Plugin.Hot.SharedProvider";
        const string pluginAssemblyName = "Plugin.Hot.SharedProviderRegistrant";
        FilterExpression filter = ItemIdFilter();
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestJson = JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
        });
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        string directory = TempDirectory("SiftQLHotSharedDirectProviderLeak");
        string providerPath = Path.Combine(directory, providerAssemblyName + ".dll");
        string pluginPath = Path.Combine(directory, pluginAssemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");

        try
        {
            Emit(
                CreateCompilation(providerAssemblyName, Source(SharedProviderSource())),
                providerPath,
                "shared provider dependency");
            Emit(
                CreateCompilation(
                    pluginAssemblyName,
                    Source(SharedProviderRegistrantSource(manifestHash, fingerprint)),
                    providerPath),
                pluginPath,
                "shared provider registrant");
            File.WriteAllText(manifestPath, manifestJson);
            using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();

            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = pluginPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
            CompiledKernel kernel = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered);
            AssertEx.True(kernel.IsTiered, "failed load removed dependency provider side effects");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static FilterExpression ItemIdFilter() =>
        FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));

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

    private static string NoOpProviderSource(string manifestHash) =>
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
                HotProviderRegistrationContext.RegisterFactory(static () => new NoOpProvider(), "{{manifestHash}}");
            }
        }

        internal sealed class NoOpProvider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                _ = subjectType;
                _ = key;
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

    private static string SharedProviderSource() =>
        """
        using System;
        using SiftQL.Hot;
        using SiftQL.Projected;

        namespace Plugin.Hot.Shared;

        public sealed class SharedProvider : IPrecompiledTieredProvider
        {
            private readonly string _fingerprint;

            public SharedProvider(string fingerprint)
            {
                _fingerprint = fingerprint;
            }

            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                if (subjectType.FullName == "SiftQL.Generators.Tests.ItemUsedEvent" &&
                    string.Equals(key, _fingerprint, StringComparison.Ordinal))
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

    private static string SharedProviderRegistrantSource(string manifestHash, string fingerprint) =>
        $$"""
        using System.Runtime.CompilerServices;
        using Plugin.Hot.Shared;
        using SiftQL.Hot;

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
                PrecompiledTieredProviderRegistry.Register(new SharedProvider("{{fingerprint}}"));
            }
        }
        """;

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        SyntaxTree source,
        params string[] extraReferences)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterCompiler).Assembly.Location);
        AddReference(references, typeof(ItemUsedEvent).Assembly.Location);
        foreach (string reference in extraReferences)
            AddReference(references, reference);
        return CSharpCompilation.Create(
            assemblyName,
            [source],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void Emit(Compilation compilation, string assemblyPath, string label)
    {
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static string TempDirectory(string prefix)
    {
        string directory = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
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

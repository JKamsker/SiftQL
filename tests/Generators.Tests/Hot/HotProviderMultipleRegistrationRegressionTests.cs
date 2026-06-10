using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderMultipleRegistrationRegressionTests
{
    [Fact]
    public void LoadingManifestAcceptsMultipleProvidersForSameManifestHash()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.MultiProviderManifest";
        FilterExpression firstFilter = Filter(nameof(ItemUsedEvent.ItemId), 100);
        FilterExpression secondFilter = Filter(nameof(ItemUsedEvent.Quantity), 2);
        string manifestJson = ManifestJson(firstFilter, secondFilter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        var compilation = GeneratorTestCompilation.Create(
            assemblyName,
            Source(RegistrationSource(
                manifestHash,
                Fingerprint(firstFilter),
                Fingerprint(secondFilter))));

        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotMultiProviderManifest",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "multi-provider manifest assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        try
        {
            using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            });

            AssertEx.True(result.Loaded, "multi-provider manifest loaded: " + result.Message);
            CompiledKernel first = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                firstFilter,
                FilterCompilerOptions.Tiered);
            CompiledKernel second = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                secondFilter,
                FilterCompilerOptions.Tiered);

            Assert.True(!first.IsTiered);
            Assert.True(!second.IsTiered);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static FilterExpression Filter(string field, long value) =>
        FilterExpression.Compare(field, FilterOperator.Equal, FilterValue.From(value));

    private static string ManifestJson(params FilterExpression[] filters)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries = filters
                .Select(filter =>
                {
                    string fingerprint = Fingerprint(filter);
                    return new HotCompilationManifestEntry
                    {
                        Key = "filter|" + typeof(ItemUsedEvent).AssemblyQualifiedName + "|" + fingerprint,
                        Kind = "filter",
                        SubjectType = typeof(ItemUsedEvent).AssemblyQualifiedName!,
                        Fingerprint = fingerprint,
                        Definition = JsonSerializer.SerializeToElement(filter),
                    };
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string RegistrationSource(
        string manifestHash,
        string firstFingerprint,
        string secondFingerprint) =>
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
                    static () => new Provider("{{firstFingerprint}}"),
                    "{{manifestHash}}");
                HotProviderRegistrationContext.RegisterFactory(
                    static () => new Provider("{{secondFingerprint}}"),
                    "{{manifestHash}}");
            }
        }

        internal sealed class Provider(string acceptedFingerprint) : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(Type subjectType, string key, out Func<object, bool>? predicate)
            {
                if (string.Equals(subjectType.FullName, "{{typeof(ItemUsedEvent).FullName}}", StringComparison.Ordinal) &&
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

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)!
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
}

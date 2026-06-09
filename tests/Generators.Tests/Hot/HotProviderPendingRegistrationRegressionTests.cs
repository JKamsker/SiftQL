using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderPendingRegistrationRegressionTests
{
    [Fact]
    public async Task FailedLoadDoesNotExposePendingManifestProviderToConcurrentCompile()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        HotProviderPendingRegistrationProbe.Reset();
        const string assemblyName = "Plugin.Hot.PendingProvider";
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(424242L));
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestJson = ManifestJson(filter);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        string directory = TempDirectory("SiftQLHotPendingProvider");
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        Task<HotTieredProviderLoadResult>? load = null;

        try
        {
            Emit(
                CreateCompilation(
                    assemblyName,
                    Source(ProviderSource(manifestHash))),
                assemblyPath,
                "pending manifest provider");
            File.WriteAllText(manifestPath, manifestJson);

            load = Task.Run(() => HotTieredProviderLoader.TryLoad(new()
            {
                AssemblyPath = assemblyPath,
                ManifestPath = manifestPath,
                RequireExactRuntimeVersion = false,
            }));

            Assert.True(
                HotProviderPendingRegistrationProbe.WaitForValidation(TimeSpan.FromSeconds(5)),
                "manifest validation reached the provider");

            CompiledKernel kernel = FilterCompiler.Compile(
                typeof(ItemUsedEvent),
                filter,
                FilterCompilerOptions.Tiered);

            AssertEx.True(kernel.IsTiered, "pending failed-load provider was not visible to compilation");
        }
        finally
        {
            HotProviderPendingRegistrationProbe.ReleaseValidation();
        }

        using HotTieredProviderLoadResult result = await load!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
        TryDeleteDirectory(directory);
    }

    private static string ManifestJson(FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        return JsonSerializer.Serialize(new HotCompilationManifest
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
        });
    }

    private static string ProviderSource(string manifestHash) =>
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using SiftQL.Generators.Tests;
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
                HotProviderRegistrationContext.RegisterFactory(static () => new PendingProvider(), "{{manifestHash}}");
            }
        }

        internal sealed class PendingProvider : IPrecompiledTieredProvider
        {
            public bool TryGetFilter(
                Type subjectType,
                string key,
                out Func<object, bool>? predicate) =>
                HotProviderPendingRegistrationProbe.TryGetFilter(subjectType, key, out predicate);

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

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        SyntaxTree source)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterCompiler).Assembly.Location);
        AddReference(references, typeof(HotProviderPendingRegistrationProbe).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            [source],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void Emit(
        Compilation compilation,
        string assemblyPath,
        string label)
    {
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
    }

    private static void AddReference(
        List<MetadataReference> references,
        string path)
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

public static class HotProviderPendingRegistrationProbe
{
    private static readonly ManualResetEventSlim s_validationStarted = new();
    private static readonly ManualResetEventSlim s_releaseValidation = new();
    private static int s_calls;

    public static void Reset()
    {
        s_validationStarted.Reset();
        s_releaseValidation.Reset();
        Volatile.Write(ref s_calls, 0);
    }

    public static bool WaitForValidation(TimeSpan timeout) =>
        s_validationStarted.Wait(timeout);

    public static void ReleaseValidation() =>
        s_releaseValidation.Set();

    public static bool TryGetFilter(
        Type subjectType,
        string key,
        out Func<object, bool>? predicate)
    {
        _ = subjectType;
        _ = key;
        if (Interlocked.Increment(ref s_calls) == 1)
        {
            s_validationStarted.Set();
            s_releaseValidation.Wait(TimeSpan.FromSeconds(10));
            predicate = null;
            return false;
        }

        predicate = static _ => true;
        return true;
    }
}

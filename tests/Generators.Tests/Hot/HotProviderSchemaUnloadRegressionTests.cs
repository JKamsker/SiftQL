using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Index;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderSchemaUnloadRegressionTests
{
    [Fact]
    public void DisposingHotProviderReleasesGeneratedSchemaProviderAssembly()
    {
        WeakReference loadContext = LoadSchemaProviderAndRelease();

        AssertCollected(loadContext);
    }

    [Fact]
    public void FailedHotProviderLoadReleasesGeneratedSchemaProviderAssembly()
    {
        WeakReference loadContext = LoadFailedSchemaProviderAndRelease();

        AssertCollected(loadContext);
    }

    [Fact]
    public void DisposingHotProviderReleasesGeneratedSchemaProviderAfterParameterizedIndexCompile()
    {
        WeakReference loadContext = LoadSchemaProviderWithParameterizedPlanAndRelease();

        AssertCollected(loadContext);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadSchemaProviderAndRelease()
    {
        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.SchemaUnload";
        const string eventTypeName = "Plugin.Events.SchemaUnloadEvent";
        var filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(1L));
        string subject = eventTypeName + ", " + assemblyName;
        string manifestJson = ManifestJson(subject, filter);
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(
            assemblyName,
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record SchemaUnloadEvent(Guid EventId, int Value) : IFilterSubject;
                """),
            manifestJson);
        Assert.Empty(diagnostics);
        AssertNoCompilationErrors(output);

        WeakReference weak;
        using (LoadedHotProvider loaded = HotProviderTestLoader.Load(
            output,
            assemblyName,
            manifestJson,
            "schema unload hot provider"))
        {
            Type eventType = loaded.Assembly.GetType(eventTypeName, throwOnError: true)!;
            _ = FilterSchema.For(eventType);
            weak = new WeakReference(AssemblyLoadContext.GetLoadContext(loaded.Assembly)!);
        }

        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadSchemaProviderWithParameterizedPlanAndRelease()
    {
        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.SchemaUnloadParameterized";
        const string eventTypeName = "Plugin.Events.SchemaUnloadParameterizedEvent";
        var filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(1L));
        string subject = eventTypeName + ", " + assemblyName;
        string manifestJson = ManifestJson(subject, filter);
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(
            assemblyName,
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record SchemaUnloadParameterizedEvent(Guid EventId, int Value) : IFilterSubject;
                """),
            manifestJson);
        Assert.Empty(diagnostics);
        AssertNoCompilationErrors(output);

        WeakReference weak;
        using (LoadedHotProvider loaded = HotProviderTestLoader.Load(
            output,
            assemblyName,
            manifestJson,
            "schema unload parameterized hot provider"))
        {
            Type eventType = loaded.Assembly.GetType(eventTypeName, throwOnError: true)!;
            var index = new FilterSubscriptionIndex<string>(eventType);
            index.Add(
                "sub",
                FilterExpression.Compare(
                    "Value",
                    FilterOperator.GreaterThan,
                    FilterValue.From(0L) with { ParameterKey = "min" }));
            _ = index.SnapshotMatches(Activator.CreateInstance(
                eventType,
                Guid.NewGuid(),
                7)!);
            weak = new WeakReference(AssemblyLoadContext.GetLoadContext(loaded.Assembly)!);
        }

        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadFailedSchemaProviderAndRelease()
    {
        using IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        const string assemblyName = "Plugin.Hot.FailedSchemaUnload";
        string probeKey = "SiftQL.FailedSchemaUnload." + Guid.NewGuid().ToString("N");
        string manifestJson = JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
        });
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        Compilation compilation = GeneratorTestCompilation.Create(
            assemblyName,
            Source(FailedLoadSource(manifestHash, probeKey)));
        AssertNoCompilationErrors(compilation);

        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotFailedSchemaUnload",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "failed schema unload assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
        Assert.Equal(HotTieredProviderLoadStatus.InvalidAssembly, result.Status);
        Assert.False(result.Loaded);
        var weak = (WeakReference?)AppDomain.CurrentDomain.GetData(probeKey);
        AppDomain.CurrentDomain.SetData(probeKey, null);
        TryDeleteDirectory(directory);
        return Assert.IsType<WeakReference>(weak);
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string assemblyName,
        SyntaxTree source,
        string manifestJson)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema-unload.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return (outputCompilation, diagnostics);
    }

    private static string ManifestJson(string subject, FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        return JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + subject + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = subject,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        });
    }

    private static string FailedLoadSource(string manifestHash, string probeKey) =>
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.Loader;
        using SiftQL;
        using SiftQL.Schema;

        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestHash", "{{manifestHash}}")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotManifestSchema", "siftql.hot.v1")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotFilterEngine", "tiered-v1")]
        [assembly: System.Reflection.AssemblyMetadata("SiftQLHotGenerator", "hot-sourcegen-v1")]

        namespace Plugin.Events;

        public sealed record FailedLoadEvent(Guid EventId, int Value) : IFilterSubject;

        internal static class FailedLoadSchemaRegistration
        {
            [ModuleInitializer]
            internal static void Register()
            {
                AppDomain.CurrentDomain.SetData(
                    "{{probeKey}}",
                    new WeakReference(AssemblyLoadContext.GetLoadContext(typeof(FailedLoadEvent).Assembly)));
                GeneratedFilterSchemaRegistry.Register(typeof(FailedLoadEvent).Assembly, TryCreate);
            }

            public static bool TryCreate(Type subjectType, out FilterSchema? schema)
            {
                if (subjectType != typeof(FailedLoadEvent))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    subjectType,
                    new FilterField[]
                    {
                        new(
                            "Value",
                            typeof(int),
                            FilterFieldKind.Scalar,
                            static subject => ((FailedLoadEvent)subject).Value,
                            new FilterScalarAccessor(
                                FilterScalarKind.Number,
                                requiredNumber: static subject => ((FailedLoadEvent)subject).Value)),
                    });
                return true;
            }
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

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch
        {
            // Best-effort cleanup; leaked collectible contexts can keep files open on Windows.
        }
    }

    private static void AssertCollected(WeakReference weak)
    {
        for (int i = 0; weak.IsAlive && i < 20; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(25);
        }

        Assert.False(weak.IsAlive);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text);
    }
}

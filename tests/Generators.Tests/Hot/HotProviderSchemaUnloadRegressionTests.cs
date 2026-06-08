using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AssertNoCompilationErrors(Compilation output)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
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

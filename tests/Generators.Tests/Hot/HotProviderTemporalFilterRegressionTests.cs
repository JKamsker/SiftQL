using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderTemporalFilterRegressionTests
{
    [Fact]
    public void GeneratedHotFilterSupportsTimestampComparisons()
    {
        const string assemblyName = "Plugin.Hot.Temporal";
        var threshold = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        FilterExpression filter = FilterExpression.Compare(
            "OccurredAt",
            FilterOperator.GreaterThanOrEqual,
            FilterValue.From(threshold));
        string manifestJson = ManifestJson(assemblyName, filter);
        GeneratorRun run = RunGenerator(assemblyName, manifestJson);

        AssertEx.Equal(0, run.Diagnostics.Count(static d => d.Id == "FSFHOT009"), "timestamp filter diagnostics");
        AssertNoCompilationErrors(run.OutputCompilation, "timestamp hot provider");

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "timestamp hot provider assembly");
        Type eventType = loaded.Assembly.GetType("Plugin.Events.TemporalEvent", throwOnError: true)!;
        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Tiered);

        Assert.False(kernel.IsTiered);
        Assert.True(kernel.Matches(Activator.CreateInstance(eventType, threshold.AddTicks(1))!));
        Assert.False(kernel.Matches(Activator.CreateInstance(eventType, threshold.AddTicks(-1))!));
    }

    private static string ManifestJson(string assemblyName, FilterExpression filter)
    {
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.TemporalEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.TemporalEvent, " + assemblyName,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static GeneratorRun RunGenerator(string assemblyName, string manifestJson)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            assemblyName,
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record TemporalEvent(DateTimeOffset OccurredAt) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("temporal.siftql-hot.json", manifestJson)),
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

    private static void AssertNoCompilationErrors(Compilation output, string label)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
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

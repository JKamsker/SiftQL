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

public sealed class HotProviderSubtypeDiscriminatorRegressionTests
{
    [Fact]
    public void GeneratedHotFilterSupportsSubjectTypesDiscriminator()
    {
        const string assemblyName = "Plugin.Hot.SubjectTypes";
        FilterExpression filter = FilterExpression.Contains(
            "subjectTypes",
            FilterValue.From("Plugin.Events.SpecialEvent"));
        string manifestJson = ManifestJson(assemblyName, filter);
        GeneratorRun run = RunGenerator(assemblyName, manifestJson);

        AssertEx.Equal(0, run.Diagnostics.Count(static d => d.Id == "FSFHOT009"), "subjectTypes diagnostics");
        AssertNoCompilationErrors(run.OutputCompilation, "subjectTypes hot provider");

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "subjectTypes hot provider assembly");
        Type baseType = loaded.Assembly.GetType("Plugin.Events.BaseEvent", throwOnError: true)!;
        Type specialType = loaded.Assembly.GetType("Plugin.Events.SpecialEvent", throwOnError: true)!;
        Type otherType = loaded.Assembly.GetType("Plugin.Events.OtherEvent", throwOnError: true)!;
        CompiledKernel kernel = FilterCompiler.Compile(baseType, filter, FilterCompilerOptions.Tiered);

        Assert.False(kernel.IsTiered);
        Assert.True(kernel.Matches(Activator.CreateInstance(specialType, 1)!));
        Assert.False(kernel.Matches(Activator.CreateInstance(otherType, 1)!));
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
                    Key = "filter|Plugin.Events.BaseEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.BaseEvent, " + assemblyName,
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
                using SiftQL;

                namespace Plugin.Events;

                public abstract record BaseEvent : IFilterSubject;
                public sealed record SpecialEvent(int Value) : BaseEvent;
                public sealed record OtherEvent(int Value) : BaseEvent;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("subject-types.siftql-hot.json", manifestJson)),
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

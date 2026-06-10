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
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderIgnoreCaseRegressionTests
{
    [Fact]
    public void GeneratedHotFilterPreservesIgnoreCaseStringComparison()
    {
        const string assemblyName = "Plugin.Hot.IgnoreCase";
        FilterExpression filter = FilterExpression.Compare(
            nameof(NamedEvent.Name),
            FilterOperator.Equal,
            FilterValue.From("boss"),
            ignoreCase: true);
        string manifestJson = ManifestJson(assemblyName, filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("ignore-case.siftql-hot.json", manifestJson));

        AssertEx.Equal(0, run.Diagnostics.Length, "ignore-case manifest diagnostics");
        AssertNoCompilationErrors(run, "ignore-case hot provider");

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "ignore-case hot provider assembly");
        Type eventType = loaded.Assembly.GetType("Plugin.Events.NamedEvent", throwOnError: true)!;
        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Tiered);

        Assert.True(!kernel.IsTiered);
        Assert.True(kernel.Matches(Activator.CreateInstance(eventType, "BOSS")!));
        Assert.False(kernel.Matches(Activator.CreateInstance(eventType, "minion")!));
    }

    private sealed record NamedEvent(string Name) : IFilterSubject;

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
                    Key = "filter|Plugin.Events.NamedEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.NamedEvent, " + assemblyName,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static GeneratorRun RunGenerator(string assemblyName, AdditionalText manifest)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(manifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(SiftQL.Schema.FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record NamedEvent(string Name) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
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

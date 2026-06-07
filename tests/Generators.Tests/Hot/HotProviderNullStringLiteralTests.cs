using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderNullStringLiteralTests
{
    [Fact]
    public void NullStringFilterLiteralDoesNotBecomeEmptyString()
    {
        const string assemblyName = "Plugin.Hot.NullString";
        var nullString = new FilterValue { Kind = FilterValueKind.String };
        var filter = FilterExpression.Compare(
            "Source",
            FilterOperator.Equal,
            nullString);
        string manifestJson = HotManifestJson(assemblyName, filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("null-string.siftql-hot.json", manifestJson),
            StringEventTree());

        AssertEx.Equal(0, run.Diagnostics.Length, "null-string generator diagnostics");
        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();
        AssertEx.Contains("String = null", source, "null-string hot provider literal");
        AssertNoCompilationErrors(run, "null-string hot provider");

        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "null-string hot provider emitted: " + string.Join(" | ", emit.Diagnostics));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        Assembly assembly = Assembly.Load(pe.ToArray());
        IPrecompiledTieredProvider provider = Provider(assembly);
        Type eventType = assembly.GetType("Plugin.Events.StringEvent", throwOnError: true)!;
        AssertEx.True(
            provider.TryGetFilter(eventType, Fingerprint(filter), out var predicate) &&
            predicate is not null,
            "null-string hot provider exposes filter");
        AssertEx.True(!predicate!(Event(eventType, string.Empty)), "null-string hot predicate rejected empty string");
        using var registration = PrecompiledTieredProviderRegistry.Register(provider);

        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Tiered);
        AssertEx.True(!kernel.IsTiered, "null-string hot provider supplied filter");
        AssertEx.True(!kernel.Matches(Event(eventType, string.Empty)), "null-string hot filter rejected empty string");
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, params SyntaxTree[] extraTrees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: extraTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string HotManifestJson(string assemblyName, FilterExpression filter)
    {
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.StringEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.StringEvent, " + assemblyName,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static IPrecompiledTieredProvider Provider(Assembly assembly)
    {
        Type providerType = assembly.GetTypes()
            .Single(type => typeof(IPrecompiledTieredProvider).IsAssignableFrom(type));
        return (IPrecompiledTieredProvider)Activator.CreateInstance(providerType, nonPublic: true)!;
    }

    private static object Event(Type eventType, string? source) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), source)!;

    private static SyntaxTree StringEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record StringEvent(Guid EventId, string? Source) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

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
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

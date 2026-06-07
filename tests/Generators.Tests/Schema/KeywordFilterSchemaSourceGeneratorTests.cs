using System.Collections.Immutable;
using SiftQL;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

internal static class KeywordFilterSchemaSourceGeneratorTests
{
    private const string CurrentProviderHint = "GeneratedCurrentFilterSchemaProvider.g.cs";

    public static void RunAll() => GeneratorEscapesKeywordPropertyAccess();

    private static void GeneratorEscapesKeywordPropertyAccess()
    {
        GeneratorRun run = RunGenerator(KeywordEventTree());
        string source = GeneratedSource(run, CurrentProviderHint);

        AssertEx.Contains("\"class\"", source, "keyword field name emitted");
        AssertNoCompilationErrors(run, "keyword property generated provider");
    }

    private static GeneratorRun RunGenerator(SyntaxTree eventTree)
    {
        CSharpCompilation compilation = CreateCompilation(eventTree);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);

        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree eventTree)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(IFilterSubject).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);

        return CSharpCompilation.Create(
            "Plugin.Schema.Keywords",
            syntaxTrees: [eventTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree KeywordEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record KeywordEvent(
                Guid EventId,
                long @class,
                string @event) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static string GeneratedSource(GeneratorRun run, string hintName) =>
        run.Result.Results[0].GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

using System.Collections.Immutable;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaRefLikeGeneratorRegressionTests
{
    [Fact]
    public void RefLikeFilterSubjectDoesNotEmitUncompilableProvider()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.RefLikeSubject",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public ref struct RefEvent : IFilterSubject
                {
                    public int Id { get; }
                }
                """));

        AssertNoCompilationErrors(run, "ref-like schema provider");
    }

    private static GeneratorRun RunGenerator(string assemblyName, params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, trees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(outputCompilation, diagnostics);
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " errors: " + string.Join(" | ", errors.Take(8)));
    }

    private sealed record GeneratorRun(
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

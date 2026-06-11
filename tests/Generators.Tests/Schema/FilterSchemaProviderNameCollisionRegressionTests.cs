using System.Collections.Immutable;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaProviderNameCollisionRegressionTests
{
    [Fact]
    public void CurrentProviderNameDoesNotCollideWithUserType()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.ProviderNameCollision",
            Source("""
                using System;
                using SiftQL;

                namespace SiftQL
                {
                    internal static class GeneratedCurrentFilterSchemaProvider
                    {
                    }
                }

                namespace Plugin.Events
                {
                    public sealed record CollisionEvent(Guid EventId) : IFilterSubject;
                }
                """));

        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, "provider name collision errors: " + string.Join(" | ", errors.Take(8)));
    }

    private static GeneratorRun RunGenerator(string assemblyName, SyntaxTree tree)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, tree);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

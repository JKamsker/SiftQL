using System.Collections.Immutable;
using System.Reflection;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaModuleInitializerRegressionTests
{
    [Fact]
    public void FilterSchemaForRunsCurrentProviderModuleInitializerBeforeFallback()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.ModuleInitializer",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record Location(long MapId);

                public sealed record MovedEvent(
                    Guid EventId,
                    Location Location) : IFilterSubject;
                """));
        AssertNoCompilationErrors(run, "module initializer schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "module initializer schema provider");
        Type eventType = assembly.GetType("Plugin.Events.MovedEvent", throwOnError: true)!;

        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("Location.MapId", out _));
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
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static Assembly EmitAndLoad(Compilation output, string label)
    {
        using var pe = new MemoryStream();
        var emit = output.Emit(pe);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
    }

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " errors: " + string.Join(" | ", errors.Take(8)));
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

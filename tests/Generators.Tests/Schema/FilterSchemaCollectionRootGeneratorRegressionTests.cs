using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaCollectionRootGeneratorRegressionTests
{
    [Fact]
    public void GeneratedSchemaIncludesObjectCollectionRootField()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.ObjectCollectionRoot",
            CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record InventoryItem(int Quantity);
                public sealed record InventoryEvent(InventoryItem[] Items) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        AssertNoCompilationErrors(run, "object collection root schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "object collection root schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.InventoryEvent", throwOnError: true)!;

        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("Items", out FilterField field), "object collection root field registered");
        AssertEx.Equal(FilterFieldKind.Array, field.Kind, "object collection root is an array field");
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

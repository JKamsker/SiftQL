using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Translation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaGeneratedSubtypeMergeRegressionTests
{
    [Fact]
    public void GeneratedObjectMergeAddsRegisteredSubtypeFields()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.GeneratedSubtypeMerge",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public abstract record Actor(string Tag);

                public sealed record Player(int Level) : Actor("player");

                public sealed record Monster(int Threat) : Actor("monster");

                public sealed record Combat(Actor? Actor) : IFilterSubject;
                """));
        AssertNoCompilationErrors(run, "generated subtype merge provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "generated subtype merge provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type playerType = assembly.GetType("Plugin.Events.Player", throwOnError: true)!;
        Type combatType = assembly.GetType("Plugin.Events.Combat", throwOnError: true)!;
        string fieldName = "Actor." + SubtypeProjection.Segment(playerType) + ".Level";
        object combat = Activator.CreateInstance(
            combatType,
            Activator.CreateInstance(playerType, 8)!)!;

        FilterSchema.RegisterValueObject(playerType);
        FilterSchema schema = FilterSchema.For(combatType);
        var filter = FilterExpression.Compare(
            fieldName,
            FilterOperator.GreaterThan,
            FilterValue.From(5L));
        CompiledKernel kernel = FilterCompiler.Compile(
            combatType,
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(schema.TryGetField(fieldName, out FilterField? field));
        Assert.Equal(typeof(int), field!.ValueType);
        Assert.True(kernel.Matches(combat));
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

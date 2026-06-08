using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaNestedRegressionTests
{
    [Fact]
    public void NestedUnreadableHiddenPropertyUsesReadableBaseSchemaField()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.NestedHiddenUnreadable",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public record BaseLocation
                {
                    public string Code { get; } = "base";
                }

                public sealed record DerivedLocation : BaseLocation
                {
                    public new int Code { set { } }
                }

                public sealed record MovedEvent(
                    Guid EventId,
                    DerivedLocation Location) : IFilterSubject;
                """));

        AssertNoCompilationErrors(run, "nested hidden unreadable property schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "nested hidden unreadable property schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.MovedEvent", throwOnError: true)!;
        Type locationType = assembly.GetType("Plugin.Events.DerivedLocation", throwOnError: true)!;
        object ev = Activator.CreateInstance(eventType, Guid.NewGuid(), Activator.CreateInstance(locationType)!)!;

        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("Location.Code", out FilterField? field));
        Assert.Equal("base", field.Getter(ev));
    }

    [Fact]
    public void GeneratedSchemaExpandsRegisteredValueObjectNestedUnderGeneratedRecord()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.NestedRegisteredValueObject",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed class ManualPoint
                {
                    public int X { get; init; }
                }

                public sealed record Location(ManualPoint Point);

                public sealed record MovedEvent(
                    Guid EventId,
                    Location Location) : IFilterSubject;
                """));

        AssertNoCompilationErrors(run, "nested registered value object schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "nested registered value object schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type pointType = assembly.GetType("Plugin.Events.ManualPoint", throwOnError: true)!;
        Type eventType = assembly.GetType("Plugin.Events.MovedEvent", throwOnError: true)!;

        FilterSchema.RegisterValueObject(pointType);
        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("Location.Point.X", out _));
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

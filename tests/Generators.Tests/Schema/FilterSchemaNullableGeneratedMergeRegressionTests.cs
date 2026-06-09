using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaNullableGeneratedMergeRegressionTests
{
    [Fact]
    public void UnrelatedValueObjectRegistrationExpandsNullableGeneratedReferenceObject()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.NullableGeneratedReferenceMerge",
            CSharpSyntaxTree.ParseText("""
                #nullable enable
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed class ManualValueObject
                {
                    public int X { get; init; }
                }

                public sealed record PlayerLocation(long MapId);

                public sealed record MovedEvent(
                    Guid EventId,
                    PlayerLocation? Location) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        AssertNoCompilationErrors(run, "nullable generated reference schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "nullable generated reference schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type manualValueObject = assembly.GetType("Plugin.Events.ManualValueObject", throwOnError: true)!;
        Type eventType = assembly.GetType("Plugin.Events.MovedEvent", throwOnError: true)!;

        FilterSchema.RegisterValueObject(manualValueObject);
        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("Location", out _));
        // Nullable reference value objects expand with null-propagating
        // accessors; only Nullable<T> value objects stay unexpanded.
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
            out _);
        return new(outputCompilation);
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

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

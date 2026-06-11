using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaCollectionRegisteredValueObjectRegressionTests
{
    [Fact]
    public void GeneratedCollectionFieldsMergeRegisteredNestedValueObjects()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.CollectionRegisteredValueObject",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed class ManualPoint
                {
                    public int X { get; init; }
                }

                public sealed record InventoryItem(ManualPoint Point);

                public sealed record InventoryEvent(InventoryItem[] Items) : IFilterSubject;
                """));

        AssertNoCompilationErrors(run, "collection registered value object schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "collection registered value object schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type pointType = assembly.GetType("Plugin.Events.ManualPoint", throwOnError: true)!;
        Type itemType = assembly.GetType("Plugin.Events.InventoryItem", throwOnError: true)!;
        Type eventType = assembly.GetType("Plugin.Events.InventoryEvent", throwOnError: true)!;
        object ev = CreateEvent(pointType, itemType, eventType);

        FilterSchema.RegisterValueObject(pointType);
        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("Items.Point.X", out FilterField? field));
        Assert.Equal(new object?[] { 7, 11 }, Assert.IsType<object?[]>(field!.Getter(ev)));
    }

    private static object CreateEvent(Type pointType, Type itemType, Type eventType)
    {
        Array items = Array.CreateInstance(itemType, 2);
        items.SetValue(Activator.CreateInstance(itemType, Point(pointType, 7))!, 0);
        items.SetValue(Activator.CreateInstance(itemType, Point(pointType, 11))!, 1);
        return Activator.CreateInstance(eventType, items)!;
    }

    private static object Point(Type pointType, int x)
    {
        object point = Activator.CreateInstance(pointType)!;
        pointType.GetProperty("X")!.SetValue(point, x);
        return point;
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

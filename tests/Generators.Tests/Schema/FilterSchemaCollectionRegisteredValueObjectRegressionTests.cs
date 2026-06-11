using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Kernel;
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

    [Fact]
    public void GeneratedCollectionMergeUsesDeclaredNestedValueObjectMembers()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.CollectionRegisteredDeclaredMember",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public class ManualBase
                {
                    public string Code { get; init; } = "";
                }

                public sealed class ManualDerived : ManualBase
                {
                    public new int Code { get; init; }
                }

                public sealed record InventoryItem(ManualBase Point);

                public sealed record InventoryEvent(InventoryItem[] Items) : IFilterSubject;
                """));

        AssertNoCompilationErrors(run, "collection registered declared member schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "collection registered declared member schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type baseType = assembly.GetType("Plugin.Events.ManualBase", throwOnError: true)!;
        Type derivedType = assembly.GetType("Plugin.Events.ManualDerived", throwOnError: true)!;
        Type itemType = assembly.GetType("Plugin.Events.InventoryItem", throwOnError: true)!;
        Type eventType = assembly.GetType("Plugin.Events.InventoryEvent", throwOnError: true)!;
        object ev = CreateDeclaredMemberEvent(baseType, derivedType, itemType, eventType);

        FilterSchema.RegisterValueObject(baseType);
        FilterSchema schema = FilterSchema.For(eventType);
        var filter = FilterExpression.Contains("Items.Point.Code", FilterValue.From("base-code"));
        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Immediate);

        Assert.True(schema.TryGetField("Items.Point.Code", out FilterField? field));
        Assert.Equal(typeof(string), field!.ValueType);
        Assert.Equal(new object?[] { "base-code" }, Assert.IsType<object?[]>(field.Getter(ev)));
        Assert.True(kernel.Matches(ev));
    }

    [Fact]
    public void GeneratedCollectionMergeUsesDeclaredCollectionWhenSubjectHidesUnsupportedProperty()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.CollectionRegisteredHiddenCollection",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed class ManualPoint
                {
                    public int X { get; init; }
                }

                public sealed record InventoryItem(ManualPoint Point);

                public abstract record InventoryBase(InventoryItem[] Items) : IFilterSubject;

                public sealed record InventoryEvent(InventoryItem[] BaseItems) : InventoryBase(BaseItems)
                {
                    public new object Items { get; } = new();
                }
                """));

        AssertNoCompilationErrors(run, "hidden collection schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "hidden collection schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type pointType = assembly.GetType("Plugin.Events.ManualPoint", throwOnError: true)!;
        Type itemType = assembly.GetType("Plugin.Events.InventoryItem", throwOnError: true)!;
        Type eventType = assembly.GetType("Plugin.Events.InventoryEvent", throwOnError: true)!;
        object ev = CreateEvent(pointType, itemType, eventType);

        FilterSchema.RegisterValueObject(pointType);
        FilterSchema schema = FilterSchema.For(eventType);
        var filter = FilterExpression.Contains("Items.Point.X", FilterValue.From(7L));
        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Immediate);

        Assert.True(schema.TryGetField("Items.Point.X", out FilterField? field));
        Assert.Equal(new object?[] { 7, 11 }, Assert.IsType<object?[]>(field!.Getter(ev)));
        Assert.True(kernel.Matches(ev));
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

    private static object CreateDeclaredMemberEvent(
        Type baseType,
        Type derivedType,
        Type itemType,
        Type eventType)
    {
        object point = Activator.CreateInstance(derivedType)!;
        baseType.GetProperty("Code", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!
            .SetValue(point, "base-code");
        derivedType.GetProperty("Code", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!
            .SetValue(point, 7);
        Array items = Array.CreateInstance(itemType, 1);
        items.SetValue(Activator.CreateInstance(itemType, point)!, 0);
        return Activator.CreateInstance(eventType, items)!;
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

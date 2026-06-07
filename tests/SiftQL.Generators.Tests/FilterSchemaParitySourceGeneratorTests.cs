using System.Collections.Immutable;
using System.Reflection;
using SiftQL;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

internal static class FilterSchemaParitySourceGeneratorTests
{
    public static void RunAll()
    {
        GeneratedNestedEventMetadataMatchesRuntimeTypeNames();
    }

    private static void GeneratedNestedEventMetadataMatchesRuntimeTypeNames()
    {
        GeneratorRun run = RunGenerator(NestedEventTree());
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "nested schema assembly emitted: " + string.Join(" | ", emit.Diagnostics));

        Assembly assembly = Assembly.Load(pe.ToArray());
        Type eventType = assembly.GetType("Plugin.Events.Container+NestedEvent", throwOnError: true)!;
        object ev = Activator.CreateInstance(eventType, Guid.NewGuid(), 42L)!;
        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("eventType", out var eventTypeField), "eventType field registered");
        AssertEx.True(schema.TryGetField("eventName", out var eventNameField), "eventName field registered");
        AssertEx.Equal(eventType.FullName, eventTypeField.Getter(ev), "eventType uses CLR FullName");
        AssertEx.Equal(eventType.Name, eventNameField.Getter(ev), "eventName uses CLR Name");
    }

    private static GeneratorRun RunGenerator(params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = CreateCompilation(trees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        AssertEx.Equal(0, diagnostics.Length, "schema parity generator diagnostics");
        return new GeneratorRun(outputCompilation);
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(IFilterSubject).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            "Plugin.Schema.Nested",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree NestedEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed class Container
            {
                public sealed record NestedEvent(Guid EventId, long CharacterId) : IFilterSubject;
            }
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

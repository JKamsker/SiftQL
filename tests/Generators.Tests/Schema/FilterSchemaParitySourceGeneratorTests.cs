using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL;
using SiftQL.Generators;
using SiftQL.Projected;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaParitySourceGeneratorTests
{
    [Fact]
    public void GeneratedNestedEventMetadataMatchesRuntimeTypeNames()
    {
        GeneratorRun run = RunGenerator(NestedEventTree());
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "nested schema assembly emitted: " + string.Join(" | ", emit.Diagnostics));

        Assembly assembly = Assembly.Load(pe.ToArray());
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.Container+NestedEvent", throwOnError: true)!;
        object ev = Activator.CreateInstance(eventType, Guid.NewGuid(), 42L)!;
        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("subjectType", out var eventTypeField), "subjectType field registered");
        AssertEx.True(schema.TryGetField("subjectName", out var eventNameField), "subjectName field registered");
        AssertEx.Equal(eventType.FullName, eventTypeField.Getter(ev), "subjectType uses CLR FullName");
        AssertEx.Equal(eventType.Name, eventNameField.Getter(ev), "subjectName uses CLR Name");
    }

    [Fact]
    public void GeneratedNestedReferenceAccessorsReturnNullWhenParentIsNull()
    {
        GeneratorRun run = RunGenerator(NestedReferenceEventTree());
        Assembly assembly = EmitAndLoad(run, "nested reference schema assembly");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.PlayerMovedEvent", throwOnError: true)!;
        object ev = Activator.CreateInstance(eventType, Guid.NewGuid(), null)!;
        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("Location.Country", out var country), "country field registered");
        Assert.Null(country.Getter(ev));
        Assert.Null(country.ScalarAccessor!.Text!(ev));
        Assert.Equal(ProjectedEventValueKind.Null, country.ProjectionAccessor!(ev).Kind);

        AssertEx.True(schema.TryGetField("Location.Score", out var score), "score field registered");
        Assert.Null(score.Getter(ev));
        Assert.Null(score.ScalarAccessor!.Number!(ev));
        Assert.Equal(ProjectedEventValueKind.Null, score.ProjectionAccessor!(ev).Kind);

        AssertEx.True(schema.TryGetField("Location.Tags", out var tags), "tags field registered");
        Assert.Null(tags.Getter(ev));
        Assert.False(tags.ArrayAccessor!.TextContains!(ev, "rare"));
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

    private static SyntaxTree NestedReferenceEventTree() =>
        CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PlayerLocation(string Country, int Score, string[] Tags);
            public sealed record PlayerMovedEvent(
                Guid EventId,
                PlayerLocation Location) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static Assembly EmitAndLoad(GeneratorRun run, string label)
    {
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

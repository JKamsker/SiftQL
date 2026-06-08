using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Index;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaPolymorphicMetadataRegressionTests
{
    [Fact]
    public void ConcreteBaseSubscriptionMatchesDerivedSubjectName()
    {
        var index = new FilterSubscriptionIndex<string>(typeof(BaseRegionEvent));
        index.Add(
            "derived",
            FilterExpression.Compare(
                "subjectName",
                FilterOperator.Equal,
                FilterValue.From(nameof(DerivedRegionEvent))));

        Assert.Equal(["derived"], index.SnapshotMatches(new DerivedRegionEvent("north")));
    }

    [Fact]
    public async Task ConcreteBaseProjectionUsesDerivedSubjectName()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(BaseRegionEvent),
            EventProjectionExpression.Select("subjectName"),
            RejectInclude,
            ProjectionCompilerOptions.Immediate);

        Projected.ProjectedEvent projected = await projection.ProjectAsync(
            new DerivedRegionEvent("north"),
            new object(),
            CancellationToken.None);

        Assert.Equal(nameof(DerivedRegionEvent), projected.Field("subjectName").String);
    }

    [Fact]
    public async Task ConcreteBaseProjectionUsesDerivedProjectedEventMetadata()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(BaseRegionEvent),
            EventProjectionExpression.Select(nameof(BaseRegionEvent.Region)),
            RejectInclude,
            ProjectionCompilerOptions.Immediate);

        Projected.ProjectedEvent projected = await projection.ProjectAsync(
            new DerivedRegionEvent("north"),
            new object(),
            CancellationToken.None);

        Assert.Equal(typeof(DerivedRegionEvent).FullName, projected.EventType);
        Assert.Equal(nameof(DerivedRegionEvent), projected.EventName);
    }

    [Fact]
    public void GeneratedConcreteBaseSchemaUsesDerivedSubjectName()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.PolymorphicMetadata",
            CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public class BaseRegionEvent : IFilterSubject
                {
                    public string Region { get; init; } = "";
                }

                public sealed class DerivedRegionEvent : BaseRegionEvent;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        AssertNoCompilationErrors(run, "polymorphic metadata schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "polymorphic metadata schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type baseType = assembly.GetType("Plugin.Events.BaseRegionEvent", throwOnError: true)!;
        Type derivedType = assembly.GetType("Plugin.Events.DerivedRegionEvent", throwOnError: true)!;
        object derived = Activator.CreateInstance(derivedType)!;

        FilterSchema schema = FilterSchema.For(baseType);

        Assert.True(schema.TryGetField("subjectName", out FilterField? field));
        Assert.Equal("DerivedRegionEvent", field.Getter(derived));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
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

    private class BaseRegionEvent(string region) : IFilterSubject
    {
        public string Region { get; } = region;
    }

    private sealed class DerivedRegionEvent(string region) : BaseRegionEvent(region);

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

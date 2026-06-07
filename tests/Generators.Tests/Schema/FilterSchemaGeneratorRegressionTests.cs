using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaGeneratorRegressionTests
{
    [Fact]
    public void UlongBackedEnumDoesNotEmitFastEnumAccessor()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.UnsignedEnum",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public enum HugeKind : ulong { First = 1, Last = 18446744073709551615 }
                public sealed record UlongEnumEvent(Guid EventId, HugeKind Kind) : IFilterSubject;
                """));
        string source = HotSource(run, "GeneratedCurrentFilterSchemaProvider.g.cs");

        AssertEx.Contains("\"Kind\"", source, "ulong enum field remains discoverable");
        AssertEx.DoesNotContain("EnumToInt64OrNull", source, "ulong enum fast accessor is not emitted");
        AssertNoCompilationErrors(run, "ulong enum schema provider");
    }

    [Fact]
    public void SanitizedHelperNameCollisionsStayCompilable()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.HelperCollision",
            Source("""
                using System;
                using SiftQL;

                namespace A.B_C
                {
                    public sealed record Event(Guid EventId) : IFilterSubject;
                }

                namespace A_B.C
                {
                    public sealed record Event(Guid EventId) : IFilterSubject;
                }
                """));

        AssertNoCompilationErrors(run, "schema helper collision provider");
    }

    [Fact]
    public void HiddenDerivedPropertyUsesDerivedSchemaField()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.HiddenProperty",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public class BaseEvent
                {
                    public Guid EventId { get; } = Guid.Empty;
                    public string Code { get; } = "";
                }

                public sealed class DerivedEvent : BaseEvent, IFilterSubject
                {
                    public new int Code { get; } = 42;
                }
                """));

        AssertNoCompilationErrors(run, "hidden derived property schema provider");
    }

    [Fact]
    public void NullableReferenceValueObjectDoesNotEmitUnsafeNestedFields()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.NullableReferenceValueObject",
            Source("""
                #nullable enable
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record PlayerLocation(long MapId);
                public sealed record PlayerMovedEvent(
                    Guid EventId,
                    PlayerLocation? Location) : IFilterSubject;
                """));
        string source = HotSource(run, "GeneratedCurrentFilterSchemaProvider.g.cs");

        AssertEx.Contains("\"Location\"", source, "nullable value object field remains discoverable");
        AssertEx.DoesNotContain("\"Location.MapId\"", source, "nullable value object nested path is not emitted");
        AssertNoCompilationErrors(run, "nullable reference value object schema provider");
    }

    [Fact]
    public void PartialFilterSubjectIsGeneratedOnce()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.PartialSubject",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed partial class PartialEvent : IFilterSubject
                {
                    public Guid EventId { get; } = Guid.Empty;
                }

                public sealed partial class PartialEvent : IFilterSubject
                {
                    public long CharacterId { get; } = 42;
                }
                """));
        string source = HotSource(run, "GeneratedCurrentFilterSchemaProvider.g.cs");

        AssertEx.Equal(
            1,
            CountOccurrences(source, "subjectType == typeof(global::Plugin.Events.PartialEvent)"),
            "partial subject has one generated type branch");
        AssertNoCompilationErrors(run, "partial subject schema provider");
    }

    [Fact]
    public void GeneratedSchemaSkipsCaseInsensitiveDuplicateFieldsAndMetadataCollisions()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.FieldCollision",
            Source("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed class CollisionEvent : IFilterSubject
                {
                    public Guid EventId { get; } = Guid.Empty;
                    public string subjectType { get; } = "payload";
                    public string SubjectName { get; } = "payload";
                    public int Id { get; } = 1;
                    public int ID { get; } = 2;
                }
                """));
        AssertNoCompilationErrors(run, "field collision schema provider");
        Assembly assembly = EmitAndLoad(run.OutputCompilation, "field collision schema provider");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.CollisionEvent", throwOnError: true)!;

        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("subjectType", out _), "schema keeps virtual subject type");
        AssertEx.Equal(
            1,
            schema.FieldNames.Count(static name => string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)),
            "schema keeps one case-insensitive Id field");
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

    private static string HotSource(GeneratorRun run, string hintName) =>
        run.Result.Results[0].GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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

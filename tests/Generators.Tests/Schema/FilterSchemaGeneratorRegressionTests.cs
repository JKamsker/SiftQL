using System.Collections.Immutable;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
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

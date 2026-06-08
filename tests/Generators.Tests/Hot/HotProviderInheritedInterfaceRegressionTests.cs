using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderInheritedInterfaceRegressionTests
{
    [Fact]
    public void HotFilterValidationAcceptsInheritedInterfaceProperty()
    {
        const string assemblyName = "Plugin.Hot.InheritedInterface";
        var filter = FilterExpression.Compare(
            "Region",
            FilterOperator.Equal,
            FilterValue.From("north"));
        string subjectType = "Plugin.Events.IZoneRegionEvent, " + assemblyName;
        string manifestJson = ManifestJson(subjectType, Fingerprint(filter), filter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("inherited-interface.siftql-hot.json", manifestJson),
            CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public interface IBaseRegionEvent : IFilterSubject
                {
                    string Region { get; }
                }

                public interface IZoneRegionEvent : IBaseRegionEvent
                {
                    int Zone { get; }
                }

                public sealed record ZoneRegionEvent(
                    string Region,
                    int Zone) : IZoneRegionEvent;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        AssertNoDiagnostic(run, "FSFHOT009", "inherited interface hot filter accepted");
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(outputCompilation, diagnostics);
    }

    private static string ManifestJson(
        string subjectType,
        string fingerprint,
        FilterExpression definition)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + subjectType + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = subjectType,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(definition),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string Fingerprint(FilterExpression expression) =>
        FilterExpressionFingerprint.Create(expression);

    private static void AssertNoDiagnostic(GeneratorRun run, string id, string label)
    {
        int count = run.Diagnostics.Count(item => item.Id == id);
        AssertEx.Equal(0, count, label);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

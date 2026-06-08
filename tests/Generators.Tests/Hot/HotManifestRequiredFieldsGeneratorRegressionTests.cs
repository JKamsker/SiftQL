using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotManifestRequiredFieldsGeneratorRegressionTests
{
    [Fact]
    public void GeneratorRejectsManifestMissingRuntimeVersion()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(100L));
        GeneratorRun run = RunGenerator(ManifestWithoutRuntimeVersion(filter));

        Assert.Contains(run.Diagnostics, static item => item.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    private static AdditionalText ManifestWithoutRuntimeVersion(FilterExpression filter)
    {
        string fingerprint = Fingerprint(filter);
        return new InMemoryAdditionalText(
            "missing-runtime.siftql-hot.json",
            $$"""
            {
              "Schema": "siftql.hot.v1",
              "FilterEngineVersion": "tiered-v1",
              "GeneratorVersion": "hot-sourcegen-v1",
              "Entries": [
                {
                  "Key": "filter|{{typeof(ItemUsedEvent).FullName}}|{{fingerprint}}",
                  "Kind": "filter",
                  "SubjectType": "{{typeof(ItemUsedEvent).AssemblyQualifiedName}}",
                  "Fingerprint": "{{fingerprint}}",
                  "Definition": {
                    "Kind": 4,
                    "Field": "{{nameof(ItemUsedEvent.ItemId)}}",
                    "Operator": 0,
                    "Value": { "Kind": 2, "Integer": 100 }
                  }
                }
              ]
            }
            """);
    }

    private static GeneratorRun RunGenerator(AdditionalText manifest)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create("Plugin.Hot.RequiredFields");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(manifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        _ = outputCompilation;
        return new(driver.GetRunResult(), diagnostics);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        ImmutableArray<Diagnostic> Diagnostics);
}
